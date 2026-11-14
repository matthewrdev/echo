import Foundation
import stdlib_h
import xlocale

// This reader is only used inside Data.withUnsafeBytes. No borrowed pointer escapes.
struct UTF8JSONReader {
    let bytes: UnsafeBufferPointer<UInt8>
    let locale: locale_t
    private var position = 0

    init(bytes: UnsafeBufferPointer<UInt8>, locale: locale_t) {
        self.bytes = bytes
        self.locale = locale
    }

    mutating func finish() throws {
        whitespace()
        guard position == bytes.count else { throw invalid() }
        try Task.checkCancellation()
    }

    mutating func object(_ field: (String, inout UTF8JSONReader) throws -> Void) throws {
        try expect(123)
        if consume(125) { return }
        repeat {
            let key = try string()
            try expect(58)
            try field(key, &self)
            if consume(125) { return }
            try expect(44)
        } while true
    }

    mutating func array<T>(_ element: (inout UTF8JSONReader) throws -> T) throws -> [T] {
        try expect(91)
        var result: [T] = []
        if consume(93) { return result }
        repeat {
            if result.count % 4096 == 0 { try Task.checkCancellation() }
            result.append(try element(&self))
            if consume(93) { return result }
            try expect(44)
        } while true
    }

    mutating func countArray() throws -> Int {
        try expect(91)
        var count = 0
        if consume(93) { return count }
        repeat {
            if count % 4096 == 0 { try Task.checkCancellation() }
            try skipValue()
            count += 1
            if consume(93) { return count }
            try expect(44)
        } while true
    }

    mutating func optional<T>(_ value: (inout UTF8JSONReader) throws -> T) throws -> T? {
        whitespace()
        if position < bytes.count, bytes[position] == 110 {
            try literal([110, 117, 108, 108])
            return nil
        }
        return try value(&self)
    }

    mutating func double() throws -> Double {
        let range = try number()
        try numericDelimiter()
        let start = UnsafeRawPointer(bytes.baseAddress! + range.lowerBound).assumingMemoryBound(to: CChar.self)
        let value = strtod_l(start, nil, locale)
        guard value.isFinite else { throw invalid() }
        return value
    }

    mutating func float() throws -> Float {
        let range = try number()
        try numericDelimiter()
        let start = UnsafeRawPointer(bytes.baseAddress! + range.lowerBound).assumingMemoryBound(to: CChar.self)
        let value = strtof_l(start, nil, locale)
        guard value.isFinite else { throw invalid() }
        return value
    }

    mutating func integer() throws -> Int {
        let range = try number()
        var value = 0
        let negative = bytes[range.lowerBound] == 45
        for index in (range.lowerBound + (negative ? 1 : 0))..<range.upperBound {
            let byte = bytes[index]
            if byte < 48 || byte > 57 {
                return try JSONDecoder().decode(Int.self, from: Data(bytes[range]))
            }
            let multiplied = value.multipliedReportingOverflow(by: 10)
            let added = multiplied.partialValue.subtractingReportingOverflow(Int(byte - 48))
            guard !multiplied.overflow, !added.overflow else { throw invalid() }
            value = added.partialValue
        }
        guard negative || value != Int.min else { throw invalid() }
        return negative ? value : -value
    }

    mutating func bool() throws -> Bool {
        whitespace()
        if position < bytes.count, bytes[position] == 116 {
            try literal([116, 114, 117, 101])
            return true
        }
        try literal([102, 97, 108, 115, 101])
        return false
    }

    mutating func string() throws -> String {
        whitespace()
        let start = position
        try expect(34)
        var escaped = false
        while position < bytes.count {
            let byte = bytes[position]
            position += 1
            if byte == 34 {
                if escaped { return try JSONDecoder().decode(String.self, from: Data(bytes[start..<position])) }
                guard let value = String(bytes: bytes[(start + 1)..<(position - 1)], encoding: .utf8) else {
                    throw invalid()
                }
                return value
            }
            guard byte >= 32 else { throw invalid() }
            if byte == 92 {
                escaped = true
                guard position < bytes.count else { throw invalid() }
                position += 1
            }
        }
        throw invalid()
    }

    // Small metadata objects keep their synthesized Decodable contracts.
    mutating func decode<T: Decodable>(_ type: T.Type) throws -> T {
        whitespace()
        let start = position
        try skipValue()
        return try JSONDecoder().decode(type, from: Data(bytes[start..<position]))
    }

    mutating func skipValue(nesting: Int = 0) throws {
        guard nesting < 128 else { throw invalid() }
        whitespace()
        guard position < bytes.count else { throw invalid() }
        switch bytes[position] {
        case 34: try skipString()
        case 123:
            position += 1
            if consume(125) { return }
            repeat {
                try skipString()
                try expect(58)
                try skipValue(nesting: nesting + 1)
                if consume(125) { return }
                try expect(44)
            } while true
        case 91:
            position += 1
            if consume(93) { return }
            var count = 0
            repeat {
                if count % 4096 == 0 { try Task.checkCancellation() }
                count += 1
                try skipValue(nesting: nesting + 1)
                if consume(93) { return }
                try expect(44)
            } while true
        case 116: try literal([116, 114, 117, 101])
        case 102: try literal([102, 97, 108, 115, 101])
        case 110: try literal([110, 117, 108, 108])
        default: _ = try number()
        }
    }

    // Most skipped artifact fields are ASCII; validate them without allocating strings.
    private mutating func skipString() throws {
        whitespace()
        let start = position
        try expect(34)
        var needsValidation = false
        while position < bytes.count {
            let byte = bytes[position]
            position += 1
            if byte == 34 {
                if needsValidation {
                    _ = try JSONDecoder().decode(String.self, from: Data(bytes[start..<position]))
                }
                return
            }
            guard byte >= 32 else { throw invalid() }
            if byte >= 128 { needsValidation = true }
            if byte == 92 {
                needsValidation = true
                guard position < bytes.count else { throw invalid() }
                position += 1
            }
        }
        throw invalid()
    }

    private mutating func number() throws -> Range<Int> {
        whitespace()
        let start = position
        if position < bytes.count, bytes[position] == 45 { position += 1 }
        guard position < bytes.count else { throw invalid() }
        if bytes[position] == 48 {
            position += 1
        } else {
            guard digit() else { throw invalid() }
            while digit() { position += 1 }
        }
        if position < bytes.count, bytes[position] == 46 {
            position += 1
            guard digit() else { throw invalid() }
            while digit() { position += 1 }
        }
        if position < bytes.count, bytes[position] == 101 || bytes[position] == 69 {
            position += 1
            if position < bytes.count, bytes[position] == 43 || bytes[position] == 45 { position += 1 }
            guard digit() else { throw invalid() }
            while digit() { position += 1 }
        }
        return start..<position
    }

    private func numericDelimiter() throws {
        guard position < bytes.count else { throw invalid() }
        switch bytes[position] {
        case 9, 10, 13, 32, 44, 93, 125: return
        default: throw invalid()
        }
    }

    private func digit() -> Bool {
        guard position < bytes.count else { return false }
        return bytes[position] >= 48 && bytes[position] <= 57
    }

    private mutating func literal(_ value: [UInt8]) throws {
        for byte in value {
            guard position < bytes.count, bytes[position] == byte else { throw invalid() }
            position += 1
        }
    }

    private mutating func whitespace() {
        while position < bytes.count {
            switch bytes[position] {
            case 9, 10, 13, 32: position += 1
            default: return
            }
        }
    }

    private mutating func consume(_ byte: UInt8) -> Bool {
        whitespace()
        guard position < bytes.count, bytes[position] == byte else { return false }
        position += 1
        return true
    }

    private mutating func expect(_ byte: UInt8) throws {
        guard consume(byte) else { throw invalid() }
    }

    func invalid() -> CLIError { CLIError(message: "Invalid saved JSON near byte \(position).") }
}
