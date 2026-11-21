/// Applies separable Gaussian blur to feature images.
/// The horizontal pass writes the intermediate image consumed by the vertical pass.
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
