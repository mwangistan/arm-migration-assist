#include <immintrin.h>

// Adds two 4-float vectors using x86 SSE intrinsics (not available on ARM64).
void add4(const float* a, const float* b, float* out)
{
    __m128 va = _mm_loadu_ps(a);
    __m128 vb = _mm_loadu_ps(b);
    __m128 vr = _mm_add_ps(va, vb);
    _mm_storeu_ps(out, vr);
}
