kernel void cloud_support(device const float* views [[buffer(0)]], device const float* depths [[buffer(1)]],
    device const float* settings [[buffer(2)]], device int* output [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int width = int(views[13]), height = int(views[14]);
    float depth = depths[id];
    if (!(depth > 0) || !isfinite(depth)) { output[id] = -1; return; }
    int x = id % width, y = id / width;
    float3 world = depth_world(views, float3((float2(x, y) - float2(width, height) * .5f) / views[3], 1) * depth);
    float3 ray = world - float3(views[0], views[1], views[2]);
    int support = 0;
    for (int i = 1; i < p[0]; i++) {
        device const float* other = views + i * 24;
        float3 otherRay = world - float3(other[0], other[1], other[2]);
        float3 crossed = cross(ray, otherRay);
        if (dot(crossed, crossed) < settings[2] * dot(ray, ray) * dot(otherRay, otherRay)) continue;
        float3 surface;
        if (!depth_surface(other, depths, world, settings[0], surface)) continue;
        float3 local = depth_local(views, surface);
        if (!(local.z > 0)) continue;
        float2 d = local.xy / local.z * views[3] + float2(width, height) * .5f - float2(x, y);
        if (dot(d, d) <= settings[1] * settings[1]) support++;
    }
    output[id] = support;
}
