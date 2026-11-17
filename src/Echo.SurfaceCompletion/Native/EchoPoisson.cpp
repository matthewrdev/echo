#include "PoissonInputStream.h"
#include "PoissonVertexStream.h"
#include "PoissonFaceStream.h"
#include <iostream>
#include <memory>

int main(int argc, char** argv)
{
    try
    {
        if (argc != 4)
        {
            throw std::runtime_error("Expected input, output and octree depth.");
        }
        const int depth = std::stoi(argv[3]);
        if (depth < 5 || depth > 12)
        {
            throw std::runtime_error("Unsupported octree depth.");
        }

        using namespace PoissonRecon;
        PoissonInputStream input(argv[1]);
        // Keep upstream ARM64 extraction serial and avoid allocating a scratch
        // array for every hardware thread when only one worker is active.
        ThreadPool::SetNumThreads(1);
        ThreadPool::ParallelizationType = ThreadPool::NONE;
        using Algorithm = Reconstructor::Poisson;
        // A zero boundary condition closes the implicit surface inside the
        // padded domain, instead of clipping missing caps at the cube boundary.
        constexpr unsigned int signature = FEMDegreeAndBType<Algorithm::DefaultFEMDegree, BOUNDARY_DIRICHLET>::Signature;
        using Signatures = IsotropicUIntPack<3, signature>;
        Algorithm::SolutionParameters<double> parameters;
        parameters.depth = depth;
        // Leave room for missing curved caps beyond the observed sample bounds.
        parameters.scale = 1.25;
        parameters.pointWeight = 4;
        parameters.alignDir = 2;

        std::cout << "Solving screened Poisson surface…" << std::endl;
        std::unique_ptr<Reconstructor::Implicit<double, 3, Signatures>> surface(
            Algorithm::Solver<double, 3, Signatures>::Solve(input, parameters));
        PoissonVertexStream vertices;
        PoissonFaceStream faces;
        Reconstructor::LevelSetExtractionParameters extraction;
        std::cout << "Extracting the completed surface…" << std::endl;
        surface->extractLevelSet(vertices, faces, extraction);

        std::ofstream output(argv[2], std::ios::binary);
        const uint32_t magic = 0x4f504345;
        const uint32_t version = 1;
        const uint64_t vertexCount = vertices.size();
        const uint64_t faceCount = faces.size();
        output.write(reinterpret_cast<const char*>(&magic), sizeof(magic));
        output.write(reinterpret_cast<const char*>(&version), sizeof(version));
        output.write(reinterpret_cast<const char*>(&vertexCount), sizeof(vertexCount));
        output.write(reinterpret_cast<const char*>(&faceCount), sizeof(faceCount));
        vertices.copyTo(output);
        faces.copyTo(output);
        output.close();
        if (!output)
        {
            throw std::runtime_error("Failed to write the completed surface.");
        }
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << std::endl;
        return 1;
    }
}
