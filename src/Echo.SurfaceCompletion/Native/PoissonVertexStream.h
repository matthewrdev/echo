#pragma once
#include "PoissonRecon/Reconstructors.h"
#include <array>

class PoissonVertexStream : public PoissonRecon::Reconstructor::OutputLevelSetVertexStream<double, 3>
{
public:
    std::vector<std::array<double, 3>> vertices;

    size_t size() const override
    {
        return vertices.size();
    }

    size_t write(const PoissonRecon::Point<double, 3>& point, const PoissonRecon::Point<double, 3>&, const double&) override
    {
        vertices.push_back({point[0], point[1], point[2]});
        return vertices.size() - 1;
    }
};
