import Foundation

// Display continuity only. The processor still validates every checkpoint before reuse.
struct SavedArtifactPresentation {
    let run: RunData
    let retainedStages: Set<PipelineStage>
    let sourceRuns: [PipelineStage: RunData]

    init(current: RunData, candidates: [RunData], artifactExists: (String) -> Bool = { _ in true }) {
        var artifacts = current.artifacts
        var sources: [PipelineStage: RunData] = [:]
        for saved in candidates
        where saved.id != current.id && saved.snapshot.id == current.snapshot.id
            && saved.storageVersion == current.storageVersion
        {
            let firstChanged = ProcessingImpact().firstChangedStage(from: saved.snapshot, to: current.snapshot)
            for stage in PipelineStage.allCases {
                if stage == firstChanged { break }
                // A newly built upstream artifact can use a different algorithm even with identical settings.
                if let chosen = artifacts[stage.rawValue], let previous = saved.artifacts[stage.rawValue],
                    chosen != previous
                {
                    break
                }
                let livePreview = current.status == "running" && artifacts[stage.rawValue + "Preview"] != nil
                let alreadyAvailable = artifacts[stage.rawValue] != nil || livePreview
                if !alreadyAvailable, let artifact = saved.artifacts[stage.rawValue], artifactExists(artifact) {
                    artifacts[stage.rawValue] = artifact
                    sources[stage] = saved
                }
            }
        }
        run = RunData(
            id: current.id, status: current.status, target: current.target, snapshot: current.snapshot,
            artifacts: artifacts, error: current.error, progress: current.progress,
            storageVersion: current.storageVersion)
        retainedStages = Set(sources.keys)
        sourceRuns = sources
    }
}
