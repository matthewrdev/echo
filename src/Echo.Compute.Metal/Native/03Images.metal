kernel void gaussian_horizontal(device const float* source [[buffer(0)]], device float* intermediate [[buffer(1)]],
    device const float* weights [[buffer(3)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int x = id % p[0], row = id - x;
    float sum = 0, correction = 0;
    for (int k = -p[2]; k <= p[2]; k++) {
        float value = source[row + clamp(x + k, 0, p[0] - 1)] * weights[k + p[2]] - correction;
        float next = sum + value; correction = (next - sum) - value; sum = next;
    }
    intermediate[id] = sum;
}
kernel void gaussian_vertical(device const float* intermediate [[buffer(1)]], device float* output [[buffer(2)]],
    device const float* weights [[buffer(3)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int x = id % p[0], y = id / p[0];
    float sum = 0, correction = 0;
    for (int k = -p[2]; k <= p[2]; k++) {
        float value = intermediate[clamp(y + k, 0, p[1] - 1) * p[0] + x] * weights[k + p[2]] - correction;
        float next = sum + value; correction = (next - sum) - value; sum = next;
    }
    output[id] = sum;
}
kernel void depth_filter(device const float* raw [[buffer(0)]], device float* depth [[buffer(1)]],
    device float* confidence [[buffer(2)]], device int* retained [[buffer(3)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    int x = id % p[0], y = id / p[0];
    retained[id] = 0;
    if (x == 0 || x == p[0] - 1 || y == 0 || y == p[1] - 1 || raw[id] <= 0) return;
    float values[9]; int count = 0;
    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) {
        float other = raw[int(id) + dy * p[0] + dx];
        if (other > 0 && abs(other - raw[id]) < raw[id] * .05f) {
            int j = count++;
            while (j > 0 && values[j - 1] > other) { values[j] = values[j - 1]; j--; }
            values[j] = other;
        }
    }
    if (count < 4) { depth[id] = 0; confidence[id] = 0; }
    else { depth[id] = values[count / 2]; retained[id] = 1; }
}
