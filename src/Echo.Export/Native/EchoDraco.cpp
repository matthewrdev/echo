#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <limits>

#include "draco/compression/decode.h"
#include "draco/compression/expert_encode.h"
#include "draco/core/decoder_buffer.h"
#include "draco/core/encoder_buffer.h"

struct EchoDracoMeshInfo
{
    void *data;
    int32_t length;
    int32_t positionId;
    int32_t uvId;
    int32_t vertices;
    int32_t triangles;
    int32_t maximumIndex;
    int32_t degenerateUvs;
    float minX;
    float minY;
    float minZ;
    float maxX;
    float maxY;
    float maxZ;
};

namespace
{
int AddAttribute(draco::Mesh &mesh, draco::GeometryAttribute::Type type, const float *data, uint8_t components)
{
    draco::GeometryAttribute attribute;
    attribute.Init(type, nullptr, components, draco::DT_FLOAT32, false, sizeof(float) * components, 0);
    const int id = mesh.AddAttribute(attribute, true, mesh.num_points());
    if (id < 0)
    {
        return -1;
    }

    auto *stored = mesh.attribute(id);
    for (uint32_t index = 0; index < mesh.num_points(); ++index)
    {
        stored->SetAttributeValue(draco::AttributeValueIndex(index), data + std::size_t(index) * components);
    }

    return id;
}

bool Measure(const draco::EncoderBuffer &encoded, EchoDracoMeshInfo &result)
{
    draco::DecoderBuffer buffer;
    buffer.Init(encoded.data(), encoded.size());
    draco::Decoder decoder;
    const auto decoded = decoder.DecodeMeshFromBuffer(&buffer);
    if (!decoded.ok())
    {
        return false;
    }

    const auto &mesh = *decoded.value();
    const auto *position = mesh.GetNamedAttribute(draco::GeometryAttribute::POSITION);
    const auto *uv = mesh.GetNamedAttribute(draco::GeometryAttribute::TEX_COORD);
    if (position == nullptr || uv == nullptr || mesh.num_points() == 0)
    {
        return false;
    }

    result.positionId = position->unique_id();
    result.uvId = uv->unique_id();
    result.vertices = mesh.num_points();
    result.triangles = mesh.num_faces();
    float minimum[3];
    position->GetMappedValue(draco::PointIndex(0), minimum);
    float maximum[3] = {minimum[0], minimum[1], minimum[2]};
    for (uint32_t index = 1; index < mesh.num_points(); ++index)
    {
        float point[3];
        position->GetMappedValue(draco::PointIndex(index), point);
        for (int axis = 0; axis < 3; ++axis)
        {
            minimum[axis] = std::min(minimum[axis], point[axis]);
            maximum[axis] = std::max(maximum[axis], point[axis]);
        }
    }

    result.minX = minimum[0];
    result.minY = minimum[1];
    result.minZ = minimum[2];
    result.maxX = maximum[0];
    result.maxY = maximum[1];
    result.maxZ = maximum[2];
    for (uint32_t index = 0; index < mesh.num_faces(); ++index)
    {
        const auto &face = mesh.face(draco::FaceIndex(index));
        float coordinates[3][2];
        for (int corner = 0; corner < 3; ++corner)
        {
            uv->GetMappedValue(face[corner], coordinates[corner]);
            result.maximumIndex = std::max(result.maximumIndex, static_cast<int32_t>(face[corner].value()));
        }

        const auto &a = coordinates[0];
        const auto &b = coordinates[1];
        const auto &c = coordinates[2];
        const double twiceArea = (double(b[0]) - a[0]) * (double(c[1]) - a[1])
            - (double(b[1]) - a[1]) * (double(c[0]) - a[0]);
        if (twiceArea == 0)
        {
            ++result.degenerateUvs;
        }
    }

    return true;
}
}

extern "C" __attribute__((visibility("default"))) int echo_draco_encode(const float *positions, const float *uvs,
    const uint32_t *indices, int32_t vertices, int32_t triangles, int32_t uvBits, EchoDracoMeshInfo *result)
{
    *result = {};
    try
    {
        draco::Mesh mesh;
        mesh.set_num_points(vertices);
        mesh.SetNumFaces(triangles);
        const int position = AddAttribute(mesh, draco::GeometryAttribute::POSITION, positions, 3);
        const int uv = AddAttribute(mesh, draco::GeometryAttribute::TEX_COORD, uvs, 2);
        if (position < 0 || uv < 0)
        {
            return 1;
        }

        for (int index = 0; index < triangles; ++index)
        {
            const auto *face = indices + std::size_t(index) * 3;
            mesh.SetFace(draco::FaceIndex(index),
                {draco::PointIndex(face[0]), draco::PointIndex(face[1]), draco::PointIndex(face[2])});
        }

        mesh.DeduplicateAttributeValues();
        mesh.DeduplicatePointIds();
        draco::ExpertEncoder encoder(mesh);
        encoder.SetEncodingMethod(draco::MESH_EDGEBREAKER_ENCODING);
        encoder.SetSpeedOptions(5, 5);
        encoder.SetAttributeQuantization(position, 14);
        if (uvBits > 0)
        {
            encoder.SetAttributeQuantization(uv, uvBits);
        }

        draco::EncoderBuffer encoded;
        if (!encoder.EncodeToBuffer(&encoded).ok()
            || encoded.size() > std::numeric_limits<int32_t>::max()
            || !Measure(encoded, *result))
        {
            return 1;
        }

        result->data = std::malloc(encoded.size());
        if (result->data == nullptr)
        {
            return 1;
        }

        std::memcpy(result->data, encoded.data(), encoded.size());
        result->length = static_cast<int32_t>(encoded.size());
        return 0;
    }
    catch (...)
    {
        return 1;
    }
}

extern "C" __attribute__((visibility("default"))) void echo_draco_free(void *data)
{
    std::free(data);
}
