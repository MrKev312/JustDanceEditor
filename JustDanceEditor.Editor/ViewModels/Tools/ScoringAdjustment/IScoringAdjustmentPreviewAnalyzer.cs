using JustDanceEditor.Formats.JDI;
using JustDanceEditor.Scoring;

using System.Threading;
using System.Threading.Tasks;

namespace JustDanceEditor.Editor.ViewModels.Tools;

internal interface IScoringAdjustmentPreviewAnalyzer
{
    Task<ScoringAdjustmentPreviewResult> AnalyzeAsync(
        IntermediateSongPackage package,
        string rootPath,
        string moveId,
        byte[] classifierBytes,
        ScoringAdjustmentDraft draft,
        MotionRecordingScoringProfile scoringProfile,
        CancellationToken cancellationToken);
}
