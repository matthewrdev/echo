#pragma once
#include "PoissonRecon/Reconstructors.h"
#include "PoissonRecordFile.h"
#include <array>

class PoissonFaceStream : public PoissonRecon::Reconstructor::OutputFaceStream<2>
{
    PoissonRecordFile records;
    size_t count = 0;

public:
    size_t size() const override
    {
        return count;
    }
    size_t write(const std::vector<PoissonRecon::node_index_type>& polygon) override
    {
        if (polygon.size() != 3)
        {
            throw std::runtime_error("Expected a triangular Poisson surface.");
        }
        const std::array<uint32_t, 3> value = {uint32_t(polygon[0]), uint32_t(polygon[1]), uint32_t(polygon[2])};
        records.append(value.data(), sizeof(value));
        return count++;
    }
    void copyTo(std::ostream& output)
    {
        records.copyTo(output);
    }
};
