kernel void epipolar_candidates(device const float4* matches [[buffer(0)]], device const float* matrices [[buffer(1)]],
    device int* candidates [[buffer(2)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float4 m = matches[id % p[0]];
    device const float* f = matrices + (id / p[0]) * 9;
    float3 forward = float3(f[0] * m.x + f[1] * m.y + f[2], f[3] * m.x + f[4] * m.y + f[5], f[6] * m.x + f[7] * m.y + f[8]);
    float2 reverse = float2(f[0] * m.z + f[3] * m.w + f[6], f[1] * m.z + f[4] * m.w + f[7]);
    float numerator = dot(float3(m.zw, 1), forward), denominator = dot(forward.xy, forward.xy) + dot(reverse, reverse);
    float scale = abs(m.z) * (abs(f[0] * m.x) + abs(f[1] * m.y) + abs(f[2]))
        + abs(m.w) * (abs(f[3] * m.x) + abs(f[4] * m.y) + abs(f[5]))
        + abs(f[6] * m.x) + abs(f[7] * m.y) + abs(f[8]);
    // Conservative ranking only. Every competitive hypothesis is rescored in
    // double on the CPU before it may change RANSAC's consensus or stop rule.
    float guard = max(1e-12f, scale * 1e-4f);
    candidates[id] = !isfinite(numerator) || !isfinite(denominator)
        || abs(numerator) <= as_type<float>(p[2]) * sqrt(max(0.0f, denominator)) * 1.001f + guard;
}
kernel void epipolar_counts(device const int* candidates [[buffer(2)]], device int* counts [[buffer(3)]],
    constant int* p [[buffer(8)]], uint lane [[thread_index_in_threadgroup]], uint hypothesis [[threadgroup_position_in_grid]]) {
    threadgroup int partial[256];
    int count = 0;
    for (int i = lane; i < p[0]; i += 256) count += candidates[hypothesis * p[0] + i];
    partial[lane] = count;
    threadgroup_barrier(mem_flags::mem_threadgroup);
    for (uint stride = 128; stride > 0; stride >>= 1) {
        if (lane < stride) partial[lane] += partial[lane + stride];
        threadgroup_barrier(mem_flags::mem_threadgroup);
    }
    if (lane == 0) counts[hypothesis] = partial[0];
}
kernel void triangulate_pairs(device const float* views [[buffer(0)]], device const float4* matches [[buffer(1)]],
    device float4* output [[buffer(2)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    device const float* a = views; device const float* b = views + 24;
    float4 match = matches[id];
    float3 ca = float3(a[0], a[1], a[2]), cb = float3(b[0], b[1], b[2]);
    float3 da = normalize(depth_world(a, float3((match.xy - float2(a[13], a[14]) * .5f) / a[3], 1)) - ca);
    float3 db = normalize(depth_world(b, float3((match.zw - float2(b[13], b[14]) * .5f) / b[3], 1)) - cb);
    float3 baseline = cb - ca;
    float crossed = dot(da, db), determinant = 1 - crossed * crossed;
    output[id] = float4(0, 0, 0, -1);
    // Nearly parallel rays are intentionally left to the double precision path.
    if (determinant < .002f) return;
    float ta = (dot(baseline, da) - crossed * dot(baseline, db)) / determinant;
    float tb = (crossed * dot(baseline, da) - dot(baseline, db)) / determinant;
    if (!(ta > 0 && tb > 0 && ta <= 5000 && tb <= 5000)) return;
    float3 point = (ca + da * ta + cb + db * tb) * .5f;
    float3 pa = depth_local(a, point), pb = depth_local(b, point);
    if (!(pa.z > 1e-8f && pb.z > 1e-8f)) return;
    float2 ea = pa.xy / pa.z * a[3] + float2(a[13], a[14]) * .5f - match.xy;
    float2 eb = pb.xy / pb.z * b[3] + float2(b[13], b[14]) * .5f - match.zw;
    output[id] = float4(point, sqrt((dot(ea, ea) + dot(eb, eb)) * .5f));
}
