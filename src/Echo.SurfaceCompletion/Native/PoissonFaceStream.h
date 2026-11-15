#pragma once
#include "PoissonRecon/Reconstructors.h"
#include <array>

class PoissonFaceStream : public PoissonRecon::Reconstructor::OutputFaceStream<2>
{
public:
    std::vector<std::array<uint32_t, 3>> faces;

    size_t size() const override
    {
        return faces.size();
    }

    size_t write(const std::vector<PoissonRecon::node_index_type>& polygon) override
    {
        if (polygon.size() != 3)
        {
            throw std::runtime_error("Expected a triangular Poisson surface.");
        }

        faces.push_back({uint32_t(polygon[0]), uint32_t(polygon[1]), uint32_t(polygon[2])});
        return faces.size() - 1;
    }
};
