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
