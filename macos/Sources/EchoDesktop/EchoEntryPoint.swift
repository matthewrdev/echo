import Foundation

@main
struct EchoEntryPoint {
    static func main() {
        if CommandLine.arguments.dropFirst().first == "--render" {
            exit(ModelRenderApplication().run())
        }
        EchoApplication.main()
    }
}
