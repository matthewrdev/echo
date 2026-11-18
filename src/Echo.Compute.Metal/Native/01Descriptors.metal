// Parameters: left count, right count, descriptor length, reverse, first left,
// batch size. No distance matrix survives the current bounded row block.
kernel void descriptor_pairs(device const float* left [[buffer(0)]], device const float* right [[buffer(1)]],
    device float* distances [[buffer(2)]], device const float4* lines [[buffer(5)]],
    device const float2* pixels [[buffer(6)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    uint a = id / p[1] + p[4], b = id % p[1];
    if (p[6]) {
        float4 line = lines[a];
        if (line.w < 1e-15f || abs(dot(line.xy, pixels[b]) + line.z) > line.w) {
            distances[id] = INFINITY; return;
        }
    }
    float sum = 0, correction = 0, reversed = 0, reverseCorrection = 0;
    for (int k = 0; k < p[2]; k++) {
        float d = left[a * p[2] + k] - right[b * p[2] + k];
        float value = d * d - correction, next = sum + value;
        correction = (next - sum) - value;
        sum = next;
        if (p[3]) {
            d = left[a * p[2] + k] - right[b * p[2] + p[2] - 1 - k];
            value = d * d - reverseCorrection;
            next = reversed + value;
            reverseCorrection = (next - reversed) - value;
            reversed = next;
        }
    }
    distances[id] = p[3] ? min(sum, reversed) : sum;
}

kernel void descriptor_left(device const float* distances [[buffer(2)]], device float4* best [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float4 value = float4(INFINITY, 0, INFINITY, 0);
    for (int b = 0; b < p[1]; b++) {
        float d = distances[id * p[1] + b];
        if (d < value.x) { value.zw = value.xy; value.xy = float2(d, b); }
        else if (d < value.z) { value.zw = float2(d, b); }
    }
    best[id + p[4]] = value;
}

kernel void descriptor_right(device const float* distances [[buffer(2)]], device float2* best [[buffer(4)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float2 value = p[4] == 0 ? float2(INFINITY, 0) : best[id];
    for (int a = 0; a < p[5]; a++) {
        float d = distances[a * p[1] + id];
        if (d < value.x) { value = float2(d, a + p[4]); }
    }
    best[id] = value;
}

// A 16x16 pair tile shares descriptor loads, then writes only local minima.
// Each distance retains the original compensated sum in descriptor order.
inline void descriptor_accept(thread float4& value, float distance, float index) {
    if (distance < value.x) { value.zw = value.xy; value.xy = float2(distance, index); }
    else if (distance < value.z) { value.zw = float2(distance, index); }
}

kernel void descriptor_tiles(device const float* left [[buffer(0)]], device const float* right [[buffer(1)]],
    device float4* leftTiles [[buffer(2)]], device float2* rightTiles [[buffer(7)]],
    device const float4* lines [[buffer(5)]], device const float2* pixels [[buffer(6)]],
    constant int* p [[buffer(8)]], uint group [[threadgroup_position_in_grid]], uint lane [[thread_index_in_threadgroup]]) {
    threadgroup float leftBlock[512], rightBlock[512], reverseBlock[512], distances[256];
    uint columns = (p[1] + 15) / 16;
    uint firstA = group / columns * 16 + p[4], firstB = group % columns * 16;
    uint localA = lane / 16, localB = lane % 16;
    uint a = firstA + localA, b = firstB + localB;
    bool valid = a < uint(min(p[0], p[4] + p[5])) && b < uint(p[1]);
    if (valid && p[6]) {
        float4 line = lines[a];
        if (line.w < 1e-15f || abs(dot(line.xy, pixels[b]) + line.z) > line.w) { valid = false; }
    }
    float sum = 0, correction = 0, reversed = 0, reverseCorrection = 0;
    for (int first = 0; first < p[2]; first += 32) {
        for (uint index = lane; index < 512; index += 256) {
            uint row = index / 32, k = first + index % 32;
            leftBlock[index] = firstA + row < uint(min(p[0], p[4] + p[5])) && k < uint(p[2])
                ? left[(firstA + row) * p[2] + k] : 0;
            rightBlock[index] = firstB + row < uint(p[1]) && k < uint(p[2])
                ? right[(firstB + row) * p[2] + k] : 0;
            if (p[3]) {
                reverseBlock[index] = firstB + row < uint(p[1]) && k < uint(p[2])
                    ? right[(firstB + row) * p[2] + p[2] - k - 1] : 0;
            }
        }
        threadgroup_barrier(mem_flags::mem_threadgroup);
        if (valid) {
            for (int k = 0; k < min(32, p[2] - first); k++) {
                float d = leftBlock[localA * 32 + k] - rightBlock[localB * 32 + k];
                float value = d * d - correction, next = sum + value;
                correction = (next - sum) - value;
                sum = next;
                if (p[3]) {
                    d = leftBlock[localA * 32 + k] - reverseBlock[localB * 32 + k];
                    value = d * d - reverseCorrection;
                    next = reversed + value;
                    reverseCorrection = (next - reversed) - value;
                    reversed = next;
                }
            }
        }
        threadgroup_barrier(mem_flags::mem_threadgroup);
    }
    distances[lane] = valid ? (p[3] ? min(sum, reversed) : sum) : INFINITY;
    threadgroup_barrier(mem_flags::mem_threadgroup);
    if (lane < 16) {
        float4 l = float4(INFINITY, 0, INFINITY, 0);
        float2 r = float2(INFINITY, 0);
        for (uint index = 0; index < 16; index++) {
            descriptor_accept(l, distances[lane * 16 + index], firstB + index);
            float d = distances[index * 16 + lane];
            if (d < r.x) { r = float2(d, firstA + index); }
        }
        leftTiles[group * 16 + lane] = l;
        rightTiles[group * 16 + lane] = r;
    }
}

kernel void descriptor_tile_left(device const float4* tiles [[buffer(2)]], device float4* best [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    uint columns = (p[1] + 15) / 16;
    float4 value = float4(INFINITY, 0, INFINITY, 0);
    for (uint column = 0; column < columns; column++) {
        float4 part = tiles[(id / 16 * columns + column) * 16 + id % 16];
        descriptor_accept(value, part.x, part.y);
        descriptor_accept(value, part.z, part.w);
    }
    best[id + p[4]] = value;
}

kernel void descriptor_tile_right(device const float2* tiles [[buffer(7)]], device float2* best [[buffer(4)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    uint columns = (p[1] + 15) / 16, rows = (p[5] + 15) / 16;
    float2 value = p[4] == 0 ? float2(INFINITY, 0) : best[id];
    for (uint row = 0; row < rows; row++) {
        float2 part = tiles[(row * columns + id / 16) * 16 + id % 16];
        if (part.x < value.x) { value = part; }
    }
    best[id] = value;
}
