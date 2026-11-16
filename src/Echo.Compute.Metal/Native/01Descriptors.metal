#include <metal_stdlib>
using namespace metal;
#pragma clang fp contract(off)

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
