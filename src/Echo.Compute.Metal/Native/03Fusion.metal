// Fusion uploads cropped depth windows; bilinear base-pixel regions do not
// overlap between tiles, so every camera votes at most once per voxel.
float4 fusion_depth_observe(device const float* view, device const float* depths, float3 world, float relative) {
    float3 local = depth_local(view, world);
    if (!(local.z > 0) || !isfinite(local.z)) return 0;
    float2 pixel = local.xy / local.z * view[3] + float2(view[13], view[14]) * .5f;
    int2 origin = int2(view[16], view[17]), size = int2(view[18], view[19]);
    if (!(pixel.x >= origin.x && pixel.x < origin.x + size.x - 1 &&
          pixel.y >= origin.y && pixel.y < origin.y + size.y - 1)) return 0;
    int2 base = int2(pixel), tile = base - origin;
    int offset = as_type<int>(view[15]);
    float minimum = INFINITY, maximum = -INFINITY, sum = 0, weight = 0;
    bool complete = true;
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) {
        float depth = depths[offset + (tile.y + dy) * size.x + tile.x + dx];
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

kernel void fusion_integrate(device const float* views [[buffer(0)]], device const float* depths [[buffer(1)]],
    device const float4* jobs [[buffer(2)]], device float4* output [[buffer(3)]], device const float* settings [[buffer(4)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    float4 previous = p[1] ? float4(0) : output[id];
    float sum = previous.x, weight = previous.y;
    int support = int(previous.z);
    for (int i = 0; i < p[0]; i++) {
        device const float* view = views + i * 24;
        float4 observation = fusion_depth_observe(view, depths, jobs[id].xyz, settings[0]);
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
    output[id] = float4(sum, weight, support, 0);
}
