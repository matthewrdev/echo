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
