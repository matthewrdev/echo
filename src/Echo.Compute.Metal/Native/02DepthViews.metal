float3 depth_local(device const float* view, float3 world) {
    float3 d = world - float3(view[0], view[1], view[2]);
    return float3(dot(d, float3(view[4], view[5], view[6])),
        dot(d, float3(view[7], view[8], view[9])), dot(d, float3(view[10], view[11], view[12])));
}
float3 depth_world(device const float* view, float3 local) {
    return float3(view[0], view[1], view[2]) + float3(
        dot(local, float3(view[4], view[7], view[10])),
        dot(local, float3(view[5], view[8], view[11])),
        dot(local, float3(view[6], view[9], view[12])));
}
// x: signed distance; y: camera depth; z: valid; w: full coverage.
float4 depth_observe(device const float* view, device const float* depths, float3 world, float relative) {
    float3 local = depth_local(view, world);
    if (!(local.z > 0) || !isfinite(local.z)) return 0;
    int width = int(view[13]), height = int(view[14]), offset = as_type<int>(view[15]);
    float2 pixel = local.xy / local.z * view[3] + float2(width, height) * .5f;
    if (!(pixel.x >= 0 && pixel.x < width - 1 && pixel.y >= 0 && pixel.y < height - 1)) return 0;
    int2 base = int2(pixel);
    float minimum = INFINITY, maximum = -INFINITY, sum = 0, weight = 0;
    bool complete = true;
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) {
        float depth = depths[offset + (base.y + dy) * width + base.x + dx];
        if (!(depth > 0) || !isfinite(depth)) { complete = false; continue; }
        minimum = min(minimum, depth); maximum = max(maximum, depth);
        float w = (dx == 0 ? 1 - (pixel.x - base.x) : pixel.x - base.x)
            * (dy == 0 ? 1 - (pixel.y - base.y) : pixel.y - base.y);
        sum += depth * w; weight += w;
    }
    float tolerance = max(local.z * relative, local.z / view[3] * 2);
    if (weight < .5f || maximum - minimum > tolerance * 2) return 0;
    return float4(sum / weight - local.z, local.z, 1, complete ? 1 : 0);
}
bool depth_surface(device const float* view, device const float* depths, float3 world, float relative, thread float3& surface) {
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
        if (w <= 0 || !(depth > 0)) continue;
        if (!isfinite(depth) || abs(depth - local.z) >= tolerance) return false;
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
        if (!depth_surface(other, depths, world, settings[0], surface)) continue;
        float3 local = depth_local(views, surface);
        if (!(local.z > 0)) continue;
        float2 d = local.xy / local.z * views[3] + float2(width, height) * .5f - float2(x, y);
        if (dot(d, d) <= settings[1] * settings[1]) support++;
    }
    output[id] = support;
}
kernel void fusion_integrate(device const float* views [[buffer(0)]], device const float* depths [[buffer(1)]],
    device const float4* jobs [[buffer(2)]], device float2* output [[buffer(3)]], device const float* settings [[buffer(4)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float sum = 0, weight = 0;
    int support = 0;
    for (int i = 0; i < p[0]; i++) {
        device const float* view = views + i * 24;
        float4 observation = depth_observe(view, depths, jobs[id].xyz, settings[0]);
        if (!observation.z) continue;
        float uncertainty = max(observation.y * settings[0], observation.y / view[3] * 2);
        float truncation = max(settings[1] * 3, uncertainty * 2), precision = 1 / (uncertainty * uncertainty);
        if (observation.x < -truncation) continue;
        if (observation.x > truncation) {
            if (observation.w) { sum += truncation * .5f * precision; weight += .5f * precision; }
            continue;
        }
        float w = (1 - .5f * abs(observation.x) / truncation) * precision;
        sum += observation.x * w; weight += w; support++;
    }
    output[id] = float2(support >= 2 ? sum / weight : 0, support);
}
