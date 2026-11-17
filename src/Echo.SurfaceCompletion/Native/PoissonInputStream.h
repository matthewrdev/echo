#pragma once
#include "PoissonRecon/Reconstructors.h"
#include <fstream>
#include <array>

class PoissonInputStream : public PoissonRecon::Reconstructor::InputOrientedSampleStream<double, 3>
{
    std::ifstream input;
    std::array<std::array<double, 6>, 4096> buffer;
    uint64_t count = 0;
    uint64_t current = 0;
    size_t buffered = 0;
    size_t position = 0;

public:
    explicit PoissonInputStream(const char* path) : input(path, std::ios::binary)
    {
        uint32_t magic = 0;
        uint32_t version = 0;
        input.read(reinterpret_cast<char*>(&magic), sizeof(magic));
        input.read(reinterpret_cast<char*>(&version), sizeof(version));
        input.read(reinterpret_cast<char*>(&count), sizeof(count));
        const bool validHeader = input && magic == 0x49504345 && version == 1;
        const bool validCount = count >= 3 && count <= 10000000;
        if (!validHeader || !validCount)
        {
            throw std::runtime_error("Invalid oriented-sample header.");
        }
        input.seekg(0, std::ios::end);
        if (input.tellg() != std::streamoff(16 + count * sizeof(buffer[0])))
        {
            throw std::runtime_error("Invalid oriented-sample length.");
        }
        reset();
    }

    uint64_t size() const
    {
        return count;
    }

    void reset() override
    {
        input.clear();
        input.seekg(16);
        current = 0;
        buffered = 0;
        position = 0;
        if (!input)
        {
            throw std::runtime_error("Failed to reset oriented samples.");
        }
    }

    bool read(PoissonRecon::Point<double, 3>& point, PoissonRecon::Point<double, 3>& normal) override
    {
        if (current == count)
        {
            return false;
        }
        if (position == buffered)
        {
            buffered = size_t(std::min<uint64_t>(buffer.size(), count - current));
            input.read(reinterpret_cast<char*>(buffer.data()), buffered * sizeof(buffer[0]));
            if (!input)
            {
                throw std::runtime_error("Truncated oriented samples.");
            }
            position = 0;
        }
        const auto& value = buffer[position++];
        current++;
        for (unsigned int axis = 0; axis < 3; axis++)
        {
            if (!std::isfinite(value[axis]) || !std::isfinite(value[axis + 3]))
            {
                throw std::runtime_error("Non-finite oriented sample.");
            }
            point[axis] = value[axis];
            normal[axis] = value[axis + 3];
        }
        return true;
    }
};
