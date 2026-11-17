kernel void surface_visibility(device const float* views [[buffer(0)]], device const float* depths [[buffer(1)]],
    device const float4* triangles [[buffer(2)]], device int* rejected [[buffer(3)]],
    device const float* settings [[buffer(4)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float3 a = triangles[id * 3].xyz, b = triangles[id * 3 + 1].xyz, c = triangles[id * 3 + 2].xyz;
    float3 points[4] = {(a + b + c) / 3, (a + b) * .5f, (b + c) * .5f, (c + a) * .5f};
    rejected[id] = 0;
    for (int sample = 0; sample < 4; sample++) {
        float supporting = 0, contradicted = 0; int freeSpace = 0;
        for (int i = 0; i < p[0]; i++) {
            device const float* view = views + i * 24;
            float4 observation = depth_observe(view, depths, points[sample], settings[0]);
            if (!observation.z) continue;
            float tolerance = max(observation.y * settings[0] * 2, observation.y / view[3] * 4);
            float precision = 1 / (tolerance * tolerance);
            if (abs(observation.x) <= tolerance) supporting += precision;
            else if (observation.w && observation.x > tolerance) { freeSpace++; contradicted += precision; }
        }
        if (freeSpace < 3 || contradicted <= supporting) return;
    }
    rejected[id] = 1;
}
uint volume_hash(int3 key) {
    uint hash = (uint(key.x) * 73856093u) ^ (uint(key.y) * 19349663u) ^ (uint(key.z) * 83492791u);
    hash ^= hash >> 16; return hash;
}
kernel void volume_index(device atomic_int* table [[buffer(0)]], device const int4* samples [[buffer(1)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    if (p[1] == 0) {
        atomic_store_explicit(table + id, -1, memory_order_relaxed);
        return;
    }
    uint slot = volume_hash(samples[id].xyz) & uint(p[0]);
    int empty = -1;
    while (!atomic_compare_exchange_weak_explicit(table + slot, &empty, int(id), memory_order_relaxed, memory_order_relaxed)) {
        // A weak exchange may fail spuriously with an unchanged empty slot.
        if (empty != -1) slot = (slot + 1) & uint(p[0]);
        empty = -1;
    }
}
kernel void volume_corners(device const int* table [[buffer(0)]], device const int4* samples [[buffer(1)]],
    device const int4* jobs [[buffer(2)]], device float* output [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    const int3 offsets[8] = {int3(0,0,0), int3(1,0,0), int3(1,1,0), int3(0,1,0),
        int3(0,0,1), int3(1,0,1), int3(1,1,1), int3(0,1,1)};
    for (int i = 0; i < 8; i++) {
        int3 key = jobs[id].xyz + offsets[i];
        uint slot = volume_hash(key) & uint(p[0]);
        int sample = table[slot];
        while (sample >= 0 && any(samples[sample].xyz != key)) {
            slot = (slot + 1) & uint(p[0]);
            sample = table[slot];
        }
        output[id * 8 + i] = sample >= 0 ? as_type<float>(samples[sample].w) : NAN;
    }
}
float depth_difference(device const float* view, device const float* depths, float3 world) {
    float3 local = depth_local(view, world);
    if (!(local.z > 0) || !isfinite(local.z)) return NAN;
    int width = int(view[13]), height = int(view[14]), offset = as_type<int>(view[15]);
    float2 pixel = local.xy / local.z * view[3] + float2(width, height) * .5f;
    if (!(pixel.x >= 0 && pixel.x < width - 1 && pixel.y >= 0 && pixel.y < height - 1)) return NAN;
    int2 base = int2(pixel); float sum = 0, weight = 0;
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) {
        float depth = depths[offset + (base.y + dy) * width + base.x + dx];
        if (depth <= 0) continue;
        float w = (dx == 0 ? 1 - (pixel.x - base.x) : pixel.x - base.x)
            * (dy == 0 ? 1 - (pixel.y - base.y) : pixel.y - base.y);
        sum += depth * w; weight += w;
    }
    return weight < .5f ? NAN : sum / weight - local.z;
}
kernel void semantic_project(device const float* views [[buffer(0)]], device const float* depths [[buffer(1)]],
    device const float4* points [[buffer(2)]], device float4* output [[buffer(3)]],
    device const uchar2* semantic [[buffer(5)]], device const float4* maps [[buffer(6)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int vertexIndex = id / p[0], image = id % p[0];
    device const float* view = views + image * 24;
    float3 world = points[vertexIndex * 2].xyz, normal = points[vertexIndex * 2 + 1].xyz;
    float3 local = depth_local(view, world);
    output[id] = 0;
    if (!(local.z > 1e-8f)) return;
    float4 map = maps[image];
    float2 uv = local.xy / local.z * (view[3] / view[13]) * float2(1, map.w) + .5f;
    if (!(uv.x > 0 && uv.y > 0 && uv.x < 1 && uv.y < 1)) return;
    float3 direction = float3(view[0], view[1], view[2]) - world;
    float facing = length(normal) > 0 ? abs(dot(normalize(normal), normalize(direction))) : 1;
    if (facing < .05f) return;
    float3 surface;
    bool measured = depth_surface(view, depths, world, .01f, surface);
    if (!measured && (p[1] == 0 || isfinite(depth_difference(view, depths, world)))) return;
    int2 sample = int2(floor(uv * map.xy - .5f));
    float2 probabilities = 0;
    if (sample.x >= 0 && sample.y >= 0 && sample.x + 1 < int(map.x) && sample.y + 1 < int(map.y)) {
        int first = as_type<int>(map.z) + sample.y * int(map.x) + sample.x;
        probabilities = float2(min(min(semantic[first], semantic[first + 1]), min(semantic[first + int(map.x)], semantic[first + int(map.x) + 1]))) / 255;
    }
    output[id] = float4(facing * view[3] / max(1e-8f, length(direction)), probabilities, 1);
}

// Broad phase only: exact double-precision segment/triangle decisions remain on
// the CPU. Outward bounds and a query-relative margin include rounding error.
kernel void surface_occlusion(device int* output [[buffer(3)]], device const float4* rays [[buffer(4)]],
    device const float4* tree [[buffer(7)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float3 from = rays[id * 2].xyz, to = rays[id * 2 + 1].xyz;
    device int* matches = output + id * p[1];
    matches[0] = -1;
    if (!all(isfinite(from)) || !all(isfinite(to))) return;
    float3 direction = to - from;
    float scale = max(max(abs(from.x), abs(from.y)), abs(from.z));
    scale = max(scale, max(max(abs(to.x), abs(to.y)), abs(to.z)));
    float margin = scale * 1e-6f + 1e-7f;
    if (!isfinite(margin) || !all(isfinite(direction))) return;
    int pending[64], size = 1, count = 0;
    pending[0] = 0;
    device const int* order = reinterpret_cast<device const int*>(tree + p[0] * 2);
    while (size > 0) {
        int node = pending[--size];
        float4 first = tree[node * 2], last = tree[node * 2 + 1];
        float3 low = first.xyz - margin, high = last.xyz + margin;
        if (!all(isfinite(low)) || !all(isfinite(high))) return;
        float near = 0, far = 1;
        bool overlaps = true;
        for (int axis = 0; axis < 3; axis++) {
            if (abs(direction[axis]) <= margin) {
                if (max(from[axis], to[axis]) < low[axis] || min(from[axis], to[axis]) > high[axis]) overlaps = false;
            } else {
                float a = (low[axis] - from[axis]) / direction[axis];
                float b = (high[axis] - from[axis]) / direction[axis];
                near = max(near, min(a, b)); far = min(far, max(a, b));
                if (near > far + 1e-5f) overlaps = false;
            }
        }
        if (!overlaps) continue;
        int left = as_type<int>(first.w), right = as_type<int>(last.w);
        if (left >= 0) {
            if (size > 61) return;
            pending[size++] = right; pending[size++] = left;
        } else {
            if (count + right >= p[1]) return;
            for (int face = 0; face < right; face++) matches[1 + count++] = order[~left + face];
        }
    }
    matches[0] = count;
}

kernel void volume_active_cells(device const float* values [[buffer(3)]], device int* offsets [[buffer(4)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    bool negative = false, positive = false, complete = true;
    for (int corner = 0; corner < 8; corner++) {
        float value = values[id * 8 + corner];
        complete = complete && isfinite(value);
        negative = negative || value < 0; positive = positive || value >= 0;
    }
    offsets[id] = complete && negative && positive ? 1 : 0;
}
kernel void volume_compact_offsets(device int* offsets [[buffer(4)]], device int* groups [[buffer(7)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int count = 0;
    int end = min(int((id + 1) * 256), p[0]);
    for (int cell = int(id * 256); cell < end; cell++) {
        int active = offsets[cell];
        offsets[cell] = active ? count++ : -1;
    }
    groups[id] = count;
}
kernel void volume_compact_groups(device int* groups [[buffer(7)]], device int* total [[buffer(5)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int count = 0;
    for (int group = 0; group < p[0]; group++) {
        int size = groups[group];
        groups[group] = count;
        count += size;
    }
    total[0] = count;
}
kernel void volume_compact_cells(device const int4* jobs [[buffer(2)]], device const float* values [[buffer(3)]],
    device const int* offsets [[buffer(4)]], device int* output [[buffer(6)]], device const int* groups [[buffer(7)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int slot = offsets[id];
    if (slot < 0) return;
    slot = (slot + groups[id / 256]) * 12;
    output[slot] = jobs[id].x; output[slot + 1] = jobs[id].y; output[slot + 2] = jobs[id].z; output[slot + 3] = 0;
    for (int corner = 0; corner < 8; corner++) output[slot + 4 + corner] = as_type<int>(values[id * 8 + corner]);
}
