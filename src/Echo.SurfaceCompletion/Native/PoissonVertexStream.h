#pragma once
#include "PoissonRecon/Reconstructors.h"
#include "PoissonRecordFile.h"
#include <array>

class PoissonVertexStream : public PoissonRecon::Reconstructor::OutputLevelSetVertexStream<double, 3>
{
    PoissonRecordFile records;
    size_t count = 0;

public:
    size_t size() const override
    {
        return count;
    }
    size_t write(const PoissonRecon::Point<double, 3>& point, const PoissonRecon::Point<double, 3>&, const double&) override
    {
        const std::array<double, 3> value = {point[0], point[1], point[2]};
        records.append(value.data(), sizeof(value));
        return count++;
    }
    void copyTo(std::ostream& output)
    {
        records.copyTo(output);
    }
};
