/// Computes an iterative warm start for the sparse texture chart UV solve.
/// Each thread updates one coordinate using the row offsets, indices and coefficients.
float uv_reduce(threadgroup float* sums, uint lane, float value) {
    sums[lane] = value;
    threadgroup_barrier(mem_flags::mem_threadgroup);
    for (uint stride = 128; stride > 0; stride >>= 1) {
        if (lane < stride) sums[lane] += sums[lane + stride];
        threadgroup_barrier(mem_flags::mem_threadgroup);
    }
    return sums[0];
}
kernel void uv_warm_start(device const int* offsets [[buffer(0)]], device const int* indices [[buffer(1)]],
    device const float* coefficients [[buffer(2)]], device float4* state [[buffer(3)]],
    device const float2* constraints [[buffer(4)]], device float* product [[buffer(5)]],
    constant int* p [[buffer(8)]], uint lane [[thread_index_in_threadgroup]]) {
    threadgroup float sums[256];
    threadgroup float energy, step, beta;
    threadgroup int stopped;
    if (lane == 0) stopped = 0;
    for (int row = lane; row < p[0]; row += 256) state[row].x = constraints[row].y;
    threadgroup_barrier(mem_flags::mem_device | mem_flags::mem_threadgroup);
    float local = 0;
    for (int row = lane; row < p[0]; row += 256) {
        float residual = 0;
        if (constraints[row].x == 0) for (int entry = offsets[row]; entry < offsets[row + 1]; entry++) residual -= coefficients[entry] * state[indices[entry]].x;
        state[row].y = residual;
        state[row].z = residual / state[row].w;
        local += residual * state[row].z;
    }
    float initial = uv_reduce(sums, lane, local);
    if (lane == 0) energy = initial;
    threadgroup_barrier(mem_flags::mem_device | mem_flags::mem_threadgroup);
    for (int iteration = 0; iteration < 128; iteration++) {
        local = 0;
        for (int row = lane; row < p[0]; row += 256) {
            float value = 0;
            if (constraints[row].x == 0) for (int entry = offsets[row]; entry < offsets[row + 1]; entry++) value += coefficients[entry] * state[indices[entry]].z;
            product[row] = value;
            local += state[row].z * value;
        }
        float denominator = uv_reduce(sums, lane, local);
        if (lane == 0) {
            if (!(denominator > 0) || !isfinite(denominator) || !(energy > max(1e-20f, initial * 1e-10f))) stopped = 1;
            step = energy / denominator;
        }
        threadgroup_barrier(mem_flags::mem_threadgroup);
        if (stopped) break;
        local = 0;
        for (int row = lane; row < p[0]; row += 256) {
            if (constraints[row].x != 0) continue;
            state[row].x += step * state[row].z;
            state[row].y -= step * product[row];
            local += state[row].y * state[row].y / state[row].w;
        }
        float next = uv_reduce(sums, lane, local);
        if (lane == 0) { beta = next / energy; energy = next; }
        threadgroup_barrier(mem_flags::mem_threadgroup);
        for (int row = lane; row < p[0]; row += 256) {
            if (constraints[row].x == 0) state[row].z = state[row].y / state[row].w + beta * state[row].z;
        }
        threadgroup_barrier(mem_flags::mem_device);
    }
}
