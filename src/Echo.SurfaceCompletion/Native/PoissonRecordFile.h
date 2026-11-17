#pragma once
#include <cstdio>
#include <cstring>
#include <array>
#include <stdexcept>
#include <ostream>

// Anonymous spool files bound extraction memory and disappear on exceptions.
class PoissonRecordFile
{
    FILE* file;
    std::array<char, 65536> buffer;
    size_t used = 0;

public:
    PoissonRecordFile() : file(std::tmpfile())
    {
        if (!file)
        {
            throw std::runtime_error("Cannot create surface spool.");
        }
    }
    PoissonRecordFile(const PoissonRecordFile&) = delete;
    PoissonRecordFile& operator=(const PoissonRecordFile&) = delete;
    ~PoissonRecordFile()
    {
        std::fclose(file);
    }
    void append(const void* record, size_t bytes)
    {
        if (used + bytes > buffer.size())
        {
            flush();
        }
        std::memcpy(buffer.data() + used, record, bytes);
        used += bytes;
    }
    void copyTo(std::ostream& output)
    {
        flush();
        if (std::fflush(file) != 0 || std::fseek(file, 0, SEEK_SET) != 0)
        {
            throw std::runtime_error("Cannot rewind surface spool.");
        }
        while (const size_t bytes = std::fread(buffer.data(), 1, buffer.size(), file))
        {
            output.write(buffer.data(), bytes);
        }
        if (std::ferror(file) || !output)
        {
            throw std::runtime_error("Cannot copy surface spool.");
        }
    }
private:
    void flush()
    {
        if (used && std::fwrite(buffer.data(), 1, used, file) != used)
        {
            throw std::runtime_error("Cannot write surface spool.");
        }
        used = 0;
    }
};
