#pragma once
#include "PoissonRecon/Reconstructors.h"
#include <fstream>
#include <array>

class PoissonInputStream : public PoissonRecon::Reconstructor::InputOrientedSampleStream<double, 3>
{
    std::vector<std::array<double, 6>> samples;
    size_t current = 0;

public:
    explicit PoissonInputStream(const char* path)
    {
        std::ifstream input(path, std::ios::binary);
        uint32_t magic = 0;
        uint32_t version = 0;
        uint64_t count = 0;
        input.read(reinterpret_cast<char*>(&magic), sizeof(magic));
        input.read(reinterpret_cast<char*>(&version), sizeof(version));
        input.read(reinterpret_cast<char*>(&count), sizeof(count));
        const bool validHeader = input && magic == 0x49504345 && version == 1;
        const bool validCount = count >= 3 && count <= 10000000;
        if (!validHeader || !validCount)
        {
            throw std::runtime_error("Invalid oriented-sample header.");
        }

        samples.resize(count);
        input.read(reinterpret_cast<char*>(samples.data()), count * sizeof(samples[0]));
        if (!input || input.peek() != std::ifstream::traits_type::eof())
        {
            throw std::runtime_error("Invalid oriented-sample length.");
        }

        for (const auto& sample : samples)
        {
            for (double value : sample)
            {
                if (!std::isfinite(value))
                {
                    throw std::runtime_error("Non-finite oriented sample.");
                }
            }
        }
    }

    void reset() override
    {
        current = 0;
    }

    bool read(PoissonRecon::Point<double, 3>& point, PoissonRecon::Point<double, 3>& normal) override
    {
        if (current == samples.size())
        {
            return false;
        }

        const auto& value = samples[current++];
        for (unsigned int axis = 0; axis < 3; axis++)
        {
            point[axis] = value[axis];
            normal[axis] = value[axis + 3];
        }
        return true;
    }
};
