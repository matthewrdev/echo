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
