import Foundation

// Display continuity only. The processor still validates every checkpoint before reuse.
struct SavedArtifactPresentation {
    let run: RunData
    let retainedStages: Set<PipelineStage>

    init(current: RunData, candidates: [RunData]) {
        var artifacts = current.artifacts
        var retained = Set<PipelineStage>()
        if current.status == "running" {
            for saved in candidates
            where saved.id != current.id && saved.snapshot.id == current.snapshot.id
                && saved.storageVersion == current.storageVersion
            {
                let firstChanged = ProcessingImpact().firstChangedStage(from: saved.snapshot, to: current.snapshot)
                for stage in PipelineStage.allCases {
                    if stage == firstChanged { break }
                    let alreadyAvailable =
                        artifacts[stage.rawValue] != nil || artifacts[stage.rawValue + "Preview"] != nil
                    if !alreadyAvailable, let artifact = saved.artifacts[stage.rawValue] {
                        artifacts[stage.rawValue] = artifact
                        retained.insert(stage)
                    }
                }
            }
        }
        run = RunData(
            id: current.id, status: current.status, target: current.target, snapshot: current.snapshot,
            artifacts: artifacts, error: current.error, progress: current.progress,
            storageVersion: current.storageVersion)
        retainedStages = retained
    }
}
