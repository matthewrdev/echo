/// Scores masked texture chart placements against the atlas occupancy.
/// The host selects the placement and updates occupancy between dispatches.
#include <metal_stdlib>
using namespace metal;

// Integer masks give CPU and GPU exactly the same collision and tie decisions.
// A thread tests one (x, bottom, rotation); atomic minimum selects the lowest
// bottom edge, then quarter turn, then x, independently of scheduling order.
kernel void texture_placement(
    device const uint *occupancy [[buffer(0)]],
    device const uint4 *masks [[buffer(1)]],
    device const uint *spans [[buffer(2)]],
    device atomic_uint *result [[buffer(3)]],
    constant uint4 &counts [[buffer(4)]],
    uint3 position [[thread_position_in_grid]])
{
    const uint size = counts.x;
    const uint wordsPerRow = counts.y;
    const uint bottom = position.y + counts.w;
    const uint rotation = position.z;
    const uint x = position.x;
    const uint4 mask = masks[rotation];
    if (bottom < mask.y || x + mask.x > size) {
        return;
    }

    const uint score = (bottom * 4 + rotation) * size + x;
    if (score >= atomic_load_explicit(result, memory_order_relaxed)) {
        return;
    }
    const uint y = bottom - mask.y;
    for (uint index = mask.z; index < mask.z + mask.w; ++index) {
        const uint row = (y + spans[index * 3]) * wordsPerRow;
        const uint start = x + spans[index * 3 + 1];
        const uint end = x + spans[index * 3 + 2];
        for (uint word = start / 32; word <= (end - 1) / 32; ++word) {
            const uint left = start > word * 32 ? start - word * 32 : 0;
            const uint right = min(32u, end - word * 32);
            const uint bits = (0xffffffffu << left) & (0xffffffffu >> (32 - right));
            if ((occupancy[row + word] & bits) != 0) {
                return;
            }
        }
    }
    atomic_fetch_min_explicit(result, score, memory_order_relaxed);
}
