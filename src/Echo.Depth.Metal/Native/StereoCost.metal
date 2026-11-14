#include <metal_stdlib>
using namespace metal;

// Deliberately avoid contracted multiply/add and hardware texture interpolation.
// Production uses float32 costs directly; the diagnostic backend also checks the CPU.
#pragma clang fp contract(off)

float sample_gray(device const float* gray, uint offset, int width, int height, float x, float y)
{
    if (!isfinite(x) || !isfinite(y) || x < 0 || y < 0 || x >= width - 1 || y >= height - 1)
    {
        return NAN;
    }
    int ix = int(x);
    int iy = int(y);
    float dx = x - ix;
    float dy = y - iy;
    uint top = offset + iy * width + ix;
    uint bottom = top + width;
    return gray[top] * (1 - dx) * (1 - dy) + gray[top + 1] * dx * (1 - dy)
        + gray[bottom] * (1 - dx) * dy + gray[bottom + 1] * dx * dy;
}

kernel void stereo_cost(
    device const float* views [[buffer(0)]],
    device const float* gray [[buffer(1)]],
    device const float* jobs [[buffer(2)]],
    device const float* depths [[buffer(3)]],
    device float* costs [[buffer(4)]],
    constant uint* counts [[buffer(5)]],
    uint index [[thread_position_in_grid]])
{
    uint pixel = index / counts[1];
    if (pixel >= counts[0])
    {
        return;
    }
    device const float* job = jobs + pixel * 32;
    float depth = depths[index];
    float worldX = job[0] + job[3] * depth;
    float worldY = job[1] + job[4] * depth;
    float worldZ = job[2] + job[5] * depth;
    float first = INFINITY;
    float second = INFINITY;
    uint support = 0;
    for (uint neighbour = 0; neighbour < counts[2]; neighbour++)
    {
        device const float* view = views + neighbour * 24;
        float x = worldX - view[0];
        float y = worldY - view[1];
        float z = worldZ - view[2];
        float px = view[3] * x + view[4] * y + view[5] * z;
        float py = view[6] * x + view[7] * y + view[8] * z;
        float pz = view[9] * x + view[10] * y + view[11] * z;
        if (pz <= 0)
        {
            continue;
        }
        float columnsX[5];
        float columnsY[5];
        float columnsZ[5];
        for (int column = 0; column < 5; column++)
        {
            float distance = (column - 2) * depth;
            columnsX[column] = px + view[12] * distance;
            columnsY[column] = py + view[13] * distance;
            columnsZ[column] = pz + view[14] * distance;
        }
        float sum = 0;
        float squared = 0;
        float dot = 0;
        bool valid = true;
        int sampleIndex = 0;
        for (int row = -2; row <= 2 && valid; row++)
        {
            float distance = row * depth;
            float downX = view[15] * distance;
            float downY = view[16] * distance;
            float downZ = view[17] * distance;
            for (int column = 0; column < 5; column++)
            {
                float qz = columnsZ[column] + downZ;
                float u = (columnsX[column] + downX) / qz * view[18] + view[19] * 0.5f;
                float v = (columnsY[column] + downY) / qz * view[18] + view[20] * 0.5f;
                float value = qz > 0 ? sample_gray(gray, as_type<uint>(view[21]), int(view[19]), int(view[20]), u, v) : NAN;
                if (!isfinite(value))
                {
                    valid = false;
                    break;
                }
                sum += value;
                squared += value * value;
                dot += job[7 + sampleIndex++] * value;
            }
        }
        float variance = squared - sum * sum / 25;
        if (!valid || variance < 0.0002f)
        {
            continue;
        }
        float cost = 1 - clamp(dot / sqrt(job[6] * variance), -1.0f, 1.0f);
        support++;
        if (cost < first)
        {
            second = first;
            first = cost;
        }
        else if (cost < second)
        {
            second = cost;
        }
    }
    costs[index] = support < min(2u, counts[2]) ? INFINITY : support == 1 ? first : (first + second) * 0.5f;
}
