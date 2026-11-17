// Stable copy-only compaction: keep accumulation and division unchanged. It is
// intentionally separate from the field kernel's algorithm fingerprint.
kernel void fusion_retained_counts(device const float4* values [[buffer(3)]],
    device uint* counts [[buffer(5)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]],
    uint local [[thread_index_in_threadgroup]], uint group [[threadgroup_position_in_grid]],
    ushort lane [[thread_index_in_simdgroup]], ushort simd [[simdgroup_index_in_threadgroup]],
    ushort width [[threads_per_simdgroup]]) {
    threadgroup uint totals[256];
    uint keep = id < uint(p[0]) && int(values[id].z) >= 2;
    uint total = simd_sum(keep);
    if (!lane) totals[simd] = total;
    threadgroup_barrier(mem_flags::mem_threadgroup);
    if (!local) {
        uint active = (min(256u, uint(p[0]) - group * 256) + width - 1) / width;
        uint sum = 0;
        for (uint i = 0; i < active; i++) sum += totals[i];
        counts[group] = sum;
    }
}

kernel void fusion_retained_offsets(device uint* counts [[buffer(5)]], device uint* total [[buffer(7)]],
    constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]]) {
    uint offset = 0;
    for (int group = 0; group < p[0]; group++) {
        uint count = counts[group];
        counts[group] = offset;
        offset += count;
    }
    total[0] = offset;
}

kernel void fusion_compact_results(device const float4* values [[buffer(3)]], device const uint* offsets [[buffer(5)]],
    device uint4* output [[buffer(6)]], constant int* p [[buffer(8)]], uint id [[thread_position_in_grid]],
    uint local [[thread_index_in_threadgroup]], uint group [[threadgroup_position_in_grid]],
    ushort lane [[thread_index_in_simdgroup]], ushort simd [[simdgroup_index_in_threadgroup]],
    ushort width [[threads_per_simdgroup]]) {
    threadgroup uint prefixes[256];
    uint keep = id < uint(p[0]) && int(values[id].z) >= 2;
    uint prefix = simd_prefix_exclusive_sum(keep);
    uint total = simd_sum(keep);
    if (!lane) prefixes[simd] = total;
    threadgroup_barrier(mem_flags::mem_threadgroup);
    if (!local) {
        uint active = (min(256u, uint(p[0]) - group * 256) + width - 1) / width;
        uint sum = 0;
        for (uint i = 0; i < active; i++) {
            uint count = prefixes[i];
            prefixes[i] = sum;
            sum += count;
        }
    }
    threadgroup_barrier(mem_flags::mem_threadgroup);
    if (keep) {
        uint4 bits = as_type<uint4>(values[id]);
        bits.w = id;
        output[offsets[group] + prefixes[simd] + prefix] = bits;
    }
}
