import Foundation

struct BinaryJournalCursor {
    let data: Data
    private(set) var offset: Int

    init(_ data: Data, offset: Int = 0) {
        self.data = data
        self.offset = offset
    }

    mutating func byte() throws -> UInt8 {
        guard offset < data.count else { throw malformed }
        defer { offset += 1 }
        return data[offset]
    }

    mutating func integer() throws -> UInt32 {
        guard data.count - offset >= 4 else { throw malformed }
        let value = data.withUnsafeBytes {
            UInt32(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: UInt32.self))
        }
        offset += 4
        return value
    }

    mutating func wideInteger() throws -> UInt64 {
        guard data.count - offset >= 8 else { throw malformed }
        let value = data.withUnsafeBytes {
            UInt64(littleEndian: $0.loadUnaligned(fromByteOffset: offset, as: UInt64.self))
        }
        offset += 8
        return value
    }

    mutating func optional<T>(_ read: (inout Self) throws -> T) throws -> T? {
        switch try byte() {
        case 0: return nil
        case 1: return try read(&self)
        default: throw malformed
        }
    }

    mutating func array<T>(_ read: (inout Self) throws -> T) throws -> [T] {
        let count = Int(try integer())
        guard count <= data.count - offset else { throw malformed }
        var values: [T] = []
        values.reserveCapacity(count)
        for _ in 0..<count {
            values.append(try read(&self))
        }
        return values
    }

    mutating func valueMap(depth: Int = 0) throws -> [String: JSONValue] {
        guard depth < 64 else { throw malformed }
        let count = Int(try integer())
        guard count <= data.count - offset else { throw malformed }
        var result: [String: JSONValue] = [:]
        for _ in 0..<count {
            let key = try string()
            result[key] = try value(depth: depth + 1)
        }
        return result
    }

    private mutating func value(depth: Int) throws -> JSONValue {
        guard depth < 64, offset < data.count else { throw malformed }
        let kind = data[offset]
        offset += 1
        switch kind {
        case 0: return .null
        case 1: return .string(try string())
        case 2: return .number(Double(Int64(bitPattern: try wideInteger())))
        case 3: return .number(Double(bitPattern: try wideInteger()))
        case 4: return .bool(false)
        case 5: return .bool(true)
        case 6: return .object(try valueMap(depth: depth))
        case 7:
            let count = Int(try integer())
            guard count <= data.count - offset else { throw malformed }
            var items: [JSONValue] = []
            for _ in 0..<count {
                items.append(try value(depth: depth + 1))
            }
            return .array(items)
        case 8: return .number(Double(try wideInteger()))
        default: throw malformed
        }
    }

    mutating func string() throws -> String {
        var length = 0
        for shift in stride(from: 0, through: 28, by: 7) {
            guard offset < data.count else { throw malformed }
            let byte = data[offset]
            offset += 1
            length |= Int(byte & 127) << shift
            if byte < 128 {
                guard length <= data.count - offset,
                    let result = String(bytes: data[offset..<(offset + length)], encoding: .utf8)
                else { throw malformed }
                offset += length
                return result
            }
        }
        throw malformed
    }

    mutating func frame() throws -> Data? {
        guard data.count - offset >= 4 else { return nil }
        let start = offset
        let length = Int(try integer())
        guard (1...64 * 1024 * 1024).contains(length) else { throw malformed }
        guard length <= data.count - offset else {
            offset = start
            return nil
        }
        let result = data.subdata(in: offset..<(offset + length))
        offset += length
        return result
    }

    private var malformed: CLIError { CLIError(message: "Invalid binary journal record.") }
}
