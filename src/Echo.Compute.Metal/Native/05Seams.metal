/// Diffuses texture-seam corrections through the seam adjacency graph.
/// Each thread computes the next correction for one node from its neighbours.
kernel void seam_diffuse(device const int* offsets [[buffer(0)]], device const int* neighbours [[buffer(1)]],
    device float4* first [[buffer(2)]], device float4* second [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    device float4* input = p[0] == 0 ? first : second;
    device float4* output = p[0] == 0 ? second : first;
    if (input[id].w > 0) { output[id] = input[id]; return; }
    float3 sum = 0;
    for (int edge = offsets[id]; edge < offsets[id + 1]; edge++) sum += input[neighbours[edge]].xyz;
    output[id] = float4(sum / (float(offsets[id + 1] - offsets[id]) + .05f), 0);
}
