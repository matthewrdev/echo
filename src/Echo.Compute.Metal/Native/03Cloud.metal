// Preserve a measured side of a depth edge without interpolating across it.
// The shared depth_surface helper remains conservative for mesh evidence.
bool cloud_surface(device const float* view, device const float* depths, float3 world, float relative, thread float3& surface) {
    float3 local = depth_local(view, world);
    if (!(local.z > 0) || !isfinite(local.z)) return false;
    int width = int(view[13]), height = int(view[14]), offset = as_type<int>(view[15]);
    float2 pixel = local.xy / local.z * view[3] + float2(width, height) * .5f;
    if (!(pixel.x >= 0 && pixel.x < width - 1 && pixel.y >= 0 && pixel.y < height - 1)) return false;
    int2 base = int2(pixel);
    float sum = 0, weight = 0, tolerance = max(local.z * relative, local.z / view[3] * 2);
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) {
        float w = (dx == 0 ? 1 - (pixel.x - base.x) : pixel.x - base.x)
            * (dy == 0 ? 1 - (pixel.y - base.y) : pixel.y - base.y);
        float depth = depths[offset + (base.y + dy) * width + base.x + dx];
        if (w <= 0 || !(depth > 0) || !isfinite(depth)) continue;
        if (abs(depth - local.z) >= tolerance) continue;
        sum += depth * w; weight += w;
    }
    if (weight < .5f) return false;
    float3 centre = float3(view[0], view[1], view[2]);
    surface = centre + (world - centre) * (sum / weight / local.z);
    return true;
}

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
        if (!cloud_surface(other, depths, world, settings[0], surface)) continue;
        float3 local = depth_local(views, surface);
        if (!(local.z > 0)) continue;
        float2 d = local.xy / local.z * views[3] + float2(width, height) * .5f - float2(x, y);
        if (dot(d, d) <= settings[1] * settings[1]) support++;
    }
    output[id] = support;
}
