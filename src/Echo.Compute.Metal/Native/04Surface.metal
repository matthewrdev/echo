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
kernel void volume_corners(device const int4* table [[buffer(0)]], device const float* values [[buffer(1)]],
    device const int4* jobs [[buffer(2)]], device float* output [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    const int3 offsets[8] = {int3(0,0,0), int3(1,0,0), int3(1,1,0), int3(0,1,0),
        int3(0,0,1), int3(1,0,1), int3(1,1,1), int3(0,1,1)};
    for (int i = 0; i < 8; i++) {
        int3 key = jobs[id].xyz + offsets[i];
        uint slot = volume_hash(key) & uint(p[0]);
        while (table[slot].w && any(table[slot].xyz != key)) slot = (slot + 1) & uint(p[0]);
        output[id * 8 + i] = table[slot].w ? values[table[slot].w - 1] : NAN;
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
