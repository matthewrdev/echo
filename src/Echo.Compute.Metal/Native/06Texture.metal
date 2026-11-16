float3 texture_sample(device const uchar4* source, int width, int height, float2 coordinate, bool swap) {
    coordinate = clamp(coordinate, float2(0), float2(width - 1, height - 1));
    int2 base = int2(coordinate), end = min(base + 1, int2(width - 1, height - 1));
    float2 f = coordinate - float2(base);
    float3 a = float3(source[base.y * width + base.x].xyz), b = float3(source[base.y * width + end.x].xyz);
    float3 c = float3(source[end.y * width + base.x].xyz), d = float3(source[end.y * width + end.x].xyz);
    uchar3 value = uchar3(clamp(a * (1 - f.x) * (1 - f.y) + b * f.x * (1 - f.y)
        + c * (1 - f.x) * f.y + d * f.x * f.y, 0.0f, 255.0f));
    return swap ? float3(value) : float3(value.zyx);
}
kernel void texture_project(device const uchar4* source [[buffer(0)]], device uchar4* atlas [[buffer(1)]],
    device const float* triangles [[buffer(2)]], device const int* offsets [[buffer(3)]],
    device const int* indices [[buffer(4)]], device const int* active [[buffer(5)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int tile = active[id / 256], local = id % 256, tilesWide = (p[0] + 15) / 16;
    int x = tile % tilesWide * 16 + local % 16, y = tile / tilesWide * 16 + local / 16;
    if (x >= p[0] || y >= p[0]) return;
    uchar4 previous = atlas[y * p[0] + x];
    for (int entry = offsets[tile]; entry < offsets[tile + 1]; entry++) {
        device const float* t = triangles + indices[entry] * 32;
        float determinant = (t[3] - t[5]) * (t[0] - t[4]) + (t[4] - t[2]) * (t[1] - t[5]);
        if (abs(determinant) < 1e-10f) continue;
        int3 sum = 0; int coverage = 0;
        for (int sy = 0; sy < 2; sy++) for (int sx = 0; sx < 2; sx++) {
            float px = x + .25f + sx * .5f, py = y + .25f + sy * .5f;
            float a = ((t[3] - t[5]) * (px - t[4]) + (t[4] - t[2]) * (py - t[5])) / determinant;
            float b = ((t[5] - t[1]) * (px - t[4]) + (t[0] - t[4]) * (py - t[5])) / determinant;
            float c = 1 - a - b;
            if (a < -1e-7f || b < -1e-7f || c < -1e-7f) continue;
            float depth = a * t[8] + b * t[11] + c * t[14];
            float2 sample = (a * float2(t[6], t[7]) * t[8] + b * float2(t[9], t[10]) * t[11]
                + c * float2(t[12], t[13]) * t[14]) / depth;
            float3 correction = a * float3(t[15], t[16], t[17]) + b * float3(t[18], t[19], t[20]) + c * float3(t[21], t[22], t[23]);
            sum += int3(clamp(texture_sample(source, p[1], p[2], sample, p[3] != 0) + correction, 0.0f, 255.0f));
            coverage++;
        }
        if (coverage) {
            int total = int(previous.w) + coverage;
            int3 rgb = (sum + int3(previous.zyx) * int(previous.w)) / total;
            previous = uchar4(uchar3(rgb.zyx), min(255, total));
        }
    }
    atlas[y * p[0] + x] = previous;
}
kernel void texture_padding(device const uchar4* source [[buffer(0)]], device uchar4* output [[buffer(1)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    uchar4 value = source[id];
    if (value.w) { output[id] = value; return; }
    int x = id % p[0], y = id / p[0], nearest = p[1] * p[1] + 1;
    // Row-major traversal preserves the CPU's first-source tie rule. A nearest
    // opaque pixel to an empty one is necessarily on the original boundary.
    for (int dy = -p[1]; dy <= p[1]; dy++) {
        int yy = y + dy;
        if (yy < 0 || yy >= p[0]) continue;
        for (int dx = -p[1]; dx <= p[1]; dx++) {
            int xx = x + dx, distance = dx * dx + dy * dy;
            if (xx < 0 || xx >= p[0] || distance >= nearest) continue;
            uchar4 candidate = source[yy * p[0] + xx];
            if (candidate.w) { value = candidate; nearest = distance; }
        }
    }
    output[id] = value;
}
