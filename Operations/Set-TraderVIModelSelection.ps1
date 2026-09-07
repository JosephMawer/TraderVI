[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ReviewDirectory,
    [ValidateSet('Prepare', 'VerifyTransaction', 'VerifyRollbackTransaction', 'Apply', 'Rollback')] [string] $Mode = 'Prepare',
    [string] $NewVersionName = 'v3.3-dated-profit-inputs',
    [ValidateSet('ADR-0056', 'ADR-0058')] [string] $NewDecisionRef = 'ADR-0056'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# No training or application launch. Prepare emits reviewable SQL; Apply/Rollback
# require explicit operational authorization and serialize against the nightly runner.
function Write-NewText([string] $Path, [string] $Text) {
    if (Test-Path -LiteralPath $Path) {
        if ([IO.File]::ReadAllText($Path) -ne $Text) { throw 'Prepared selection artifact differs; use a new review directory.' }
        return
    }
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $bytes = [Text.Encoding]::UTF8.GetBytes($Text); $stream.Write($bytes); $stream.Flush($true) }
    finally { $stream.Dispose() }
}
function Get-JsonHash([string] $Json) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::Unicode.GetBytes($Json)))
}
function Escape-Sql([string] $Value) { $Value.Replace("'", "''") }

$reviewRoot = [IO.Path]::GetFullPath($ReviewDirectory)
$previousJson = [IO.File]::ReadAllText((Join-Path $reviewRoot 'previous-model-set.json'))
$correctedJson = [IO.File]::ReadAllText((Join-Path $reviewRoot 'corrected-model-set.json'))
$strategyJson = [IO.File]::ReadAllText((Join-Path $reviewRoot 'previous-strategy.json'))
$previousSet = $previousJson | ConvertFrom-Json
$correctedSet = $correctedJson | ConvertFrom-Json
$previousId = [Guid] $previousSet.StrategyVersionId
$correctedId = [Guid] $correctedSet.StrategyVersionId
if ($previousId -eq $correctedId -or $correctedSet.InputContract -ne 'ProfitInputs.XiuDatedV1' -or
    $correctedSet.SourceIdentity -notmatch '^working-tree-sha256:[A-F0-9]{64}$' -or
    $NewVersionName.Length -gt 32 -or [string]::IsNullOrWhiteSpace($NewVersionName)) { throw 'Invalid corrected strategy identity.' }
foreach ($set in @($previousSet, $correctedSet)) {
    if ($set.Models.Count -ne 4 -or @($set.Models.Registry.ModelId | Select-Object -Unique).Count -ne 4) { throw 'Incomplete model set.' }
    foreach ($model in $set.Models) {
        if ((Get-FileHash -LiteralPath $model.Registry.ZipPath -Algorithm SHA256).Hash -ne $model.ArtifactSha256) {
            throw 'Model bytes changed after review; no selection performed.'
        }
    }
}
$previousHash = Get-JsonHash $previousJson
if ($NewDecisionRef -eq 'ADR-0058' -and
    ($previousSet.InputContract -ne 'ProfitInputs.XiuDatedV1' -or $previousSet.ModelSetId -ne $correctedSet.ModelSetId -or
     ($previousSet.Models | ConvertTo-Json -Depth 30 -Compress) -ne ($correctedSet.Models | ConvertTo-Json -Depth 30 -Compress))) {
    throw 'The benchmark repair must retain the exact predecessor models and metadata.'
}
$correctedHash = Get-JsonHash $correctedJson
$planPath = Join-Path $reviewRoot 'selection-plan.json'
if (Test-Path -LiteralPath $planPath) {
    $plan = Get-Content -LiteralPath $planPath -Raw | ConvertFrom-Json
    $plannedDecision = if ($plan.PSObject.Properties.Name -contains 'NewDecisionRef') { $plan.NewDecisionRef } else { 'ADR-0056' }
    if ($plannedDecision -ne $NewDecisionRef) { throw 'Prepared decision identity differs.' }
    if ($plan.PreviousHash -ne $previousHash -or $plan.CorrectedHash -ne $correctedHash -or $plan.NewVersionName -ne $NewVersionName) {
        throw 'Selection review changed; refusing to reuse its authorization record.'
    }
} else {
    $plan = [pscustomobject]@{ PreviousHash=$previousHash; CorrectedHash=$correctedHash; NewVersionName=$NewVersionName; NewDecisionRef=$NewDecisionRef;
        SelectionEventId=[Guid]::NewGuid().ToString('D'); RollbackEventId=[Guid]::NewGuid().ToString('D') }
    Write-NewText $planPath ($plan | ConvertTo-Json)
}

$nameSql = Escape-Sql $NewVersionName
$previousSql = Escape-Sql $previousJson
$correctedSql = Escape-Sql $correctedJson
$strategySql = Escape-Sql $strategyJson
$sourceSql = Escape-Sql $correctedSet.SourceIdentity
$reviewSql = Escape-Sql $correctedSet.ReviewReference
if ($correctedSet.ReviewReference.Length -gt 512) { throw 'Review reference is too long.' }
$descriptionSql = if ($NewDecisionRef -eq 'ADR-0058') { 'Observed XIU/SPY confirmation; same dated model set and existing thresholds.' } else { 'Dated profit inputs; reviewed replacement model set; existing thresholds preserved.' }
$noteSql = if ($NewDecisionRef -eq 'ADR-0058') { ' | ADR-0058 benchmark correctness repair; no performance-promotion claim.' } else { ' | ADR-0056/0057 correctness repair; no performance-promotion claim.' }
$registration = @"
IF EXISTS (
    SELECT VersionName,MinCompositeScore,MinDirectionProb,RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,
        MinBreakoutProb,MinDirectionEdge,MaxDownProb,BreadthVetoThreshold,StrongBreakoutOverride,StrongEdgeOverride,Notes,InitialCodeCommit,DecisionRef
    FROM dbo.StrategyVersion WHERE VersionId=@previous
    EXCEPT
    SELECT VersionName,MinCompositeScore,MinDirectionProb,RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,
        MinBreakoutProb,MinDirectionEdge,MaxDownProb,BreadthVetoThreshold,StrongBreakoutOverride,StrongEdgeOverride,Notes,InitialCodeCommit,DecisionRef
    FROM OPENJSON(N'$strategySql') WITH (
        VersionName nvarchar(32),MinCompositeScore float,MinDirectionProb float,RegressionVeto float,StopLossPercent float,WarningPercent float,MaxPositions int,
        MinBreakoutProb float,MinDirectionEdge float,MaxDownProb float,BreadthVetoThreshold float,StrongBreakoutOverride float,StrongEdgeOverride float,
        Notes nvarchar(max),InitialCodeCommit nvarchar(128),DecisionRef nvarchar(64)))
    THROW 51270, 'Predecessor strategy settings changed since review.', 1;
IF EXISTS (SELECT 1 FROM OPENJSON(@previousJson,'$.Models') item
    LEFT JOIN dbo.ModelRegistry model ON model.ModelId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(item.value,'$.Registry.ModelId'))
    WHERE model.ModelId IS NULL OR model.TaskType<>JSON_VALUE(item.value,'$.Registry.TaskType')
       OR ISNULL(model.FeatureSet,N'')<>JSON_VALUE(item.value,'$.Registry.FeatureSet')
       OR model.InputSchema<>JSON_VALUE(item.value,'$.Registry.InputSchema')
       OR model.ModelKind<>JSON_VALUE(item.value,'$.Registry.ModelKind')
       OR model.LookbackBars<>TRY_CONVERT(int,JSON_VALUE(item.value,'$.Registry.LookbackBars'))
       OR model.HorizonBars<>TRY_CONVERT(int,JSON_VALUE(item.value,'$.Registry.HorizonBars'))
       OR model.ThresholdBuy<>TRY_CONVERT(float,JSON_VALUE(item.value,'$.Registry.ThresholdBuy'))
       OR model.ThresholdSell<>TRY_CONVERT(float,JSON_VALUE(item.value,'$.Registry.ThresholdSell')))
    THROW 51269, 'Predecessor model metadata changed since preservation.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.StrategyModelBinding WHERE StrategyVersionId = @previous)
BEGIN
    -- Before the first frozen assignment, the enabled allowed-task identities must
    -- still match the captured predecessor. Ignore dormant tasks outside this set.
    IF EXISTS (
        SELECT ModelId FROM dbo.ModelRegistry WHERE IsEnabled=1 AND TaskType IN
            (SELECT JSON_VALUE(value,'$.Registry.TaskType') FROM OPENJSON(@previousJson,'$.Models'))
        EXCEPT SELECT CONVERT(uniqueidentifier,JSON_VALUE(value,'$.Registry.ModelId')) FROM OPENJSON(@previousJson,'$.Models'))
        THROW 51271, 'Enabled predecessor models changed since preservation.', 1;
    IF EXISTS (SELECT CONVERT(uniqueidentifier,JSON_VALUE(value,'$.Registry.ModelId')) FROM OPENJSON(@previousJson,'$.Models')
        EXCEPT SELECT ModelId FROM dbo.ModelRegistry WHERE IsEnabled=1)
        THROW 51272, 'A preserved predecessor is no longer enabled.', 1;
    INSERT dbo.StrategyModelBinding(StrategyVersionId,ModelSetId,ModelSetJson,ModelSetSha256)
        VALUES(@previous,CONVERT(uniqueidentifier,JSON_VALUE(@previousJson,'$.ModelSetId')),@previousJson,'$previousHash');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.StrategyModelBinding WHERE StrategyVersionId=@previous AND ModelSetSha256='$previousHash')
    THROW 51273, 'Preserved predecessor assignment differs.', 1;
IF EXISTS (SELECT 1 FROM dbo.StrategyVersion WHERE VersionId=@target OR VersionName=N'$nameSql')
    THROW 51274, 'Target identity already exists outside this completed selection event.', 1;
INSERT dbo.StrategyVersion(VersionId,VersionName,Description,IsActive,MinCompositeScore,MinDirectionProb,
    RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,Notes,InitialCodeCommit,DecisionRef,
    MinBreakoutProb,MinDirectionEdge,MaxDownProb,BreadthVetoThreshold,StrongBreakoutOverride,StrongEdgeOverride)
SELECT @target,N'$nameSql',N'$descriptionSql',0,
    MinCompositeScore,MinDirectionProb,RegressionVeto,StopLossPercent,WarningPercent,MaxPositions,
    CONCAT(Notes,N'$noteSql'),N'$sourceSql',N'$NewDecisionRef',
    MinBreakoutProb,MinDirectionEdge,MaxDownProb,BreadthVetoThreshold,StrongBreakoutOverride,StrongEdgeOverride
FROM dbo.StrategyVersion WHERE VersionId=@previous;
INSERT dbo.StrategyModelBinding(StrategyVersionId,ModelSetId,ModelSetJson,ModelSetSha256)
    VALUES(@target,CONVERT(uniqueidentifier,JSON_VALUE(@targetJson,'$.ModelSetId')),@targetJson,'$correctedHash');
IF EXISTS (SELECT 1 FROM OPENJSON(@targetJson,'$.Models') item
    LEFT JOIN dbo.ModelRegistry model ON model.ModelId=TRY_CONVERT(uniqueidentifier,JSON_VALUE(item.value,'$.Registry.ModelId'))
    WHERE model.ModelId IS NULL OR model.IsEnabled<>0
       OR model.TaskType<>JSON_VALUE(item.value,'$.Registry.TaskType')
       OR ISNULL(model.FeatureSet,N'')<>JSON_VALUE(item.value,'$.Registry.FeatureSet')
       OR model.InputSchema<>JSON_VALUE(item.value,'$.Registry.InputSchema')
       OR model.ModelKind<>JSON_VALUE(item.value,'$.Registry.ModelKind')
       OR model.LookbackBars<>TRY_CONVERT(int,JSON_VALUE(item.value,'$.Registry.LookbackBars'))
       OR model.HorizonBars<>TRY_CONVERT(int,JSON_VALUE(item.value,'$.Registry.HorizonBars'))
       OR model.ThresholdBuy<>TRY_CONVERT(float,JSON_VALUE(item.value,'$.Registry.ThresholdBuy'))
       OR model.ThresholdSell<>TRY_CONVERT(float,JSON_VALUE(item.value,'$.Registry.ThresholdSell')))
    THROW 51275, 'Candidate registry metadata changed or a candidate was independently enabled.', 1;
INSERT dbo.StrategyVersionModel(VersionId,ModelId,CompositeWeight,IsRequired,Role)
    SELECT @target,CONVERT(uniqueidentifier,JSON_VALUE(value,'$.Registry.ModelId')),
        CONVERT(float,JSON_VALUE(value,'$.CompositeWeight')),1,JSON_VALUE(value,'$.Role')
    FROM OPENJSON(@targetJson,'$.Models');
"@

function Selection-Sql([Guid] $From, [Guid] $To, [Guid] $EventId, [string] $TargetHash, [string] $RegisterSql, [string] $Review) {
@"
USE [TraderDB];
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
DECLARE @previous uniqueidentifier='$From', @target uniqueidentifier='$To', @event uniqueidentifier='$EventId';
DECLARE @previousJson nvarchar(max)=N'$previousSql', @targetJson nvarchar(max)=N'$correctedSql';
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @lockResult int;
EXEC @lockResult=sys.sp_getapplock @Resource=N'TraderVI.StrategyModelSelection',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=0;
IF @lockResult<0 THROW 51276, 'Another strategy/model selection holds the lock.', 1;
IF EXISTS(SELECT 1 FROM dbo.StrategyModelSelectionEvent WHERE EventId=@event)
BEGIN
    IF EXISTS(SELECT 1 FROM dbo.StrategyModelSelectionEvent WHERE EventId=@event AND PreviousStrategyVersionId=@previous
        AND SelectedStrategyVersionId=@target AND SelectedModelSetSha256='$TargetHash')
        AND (SELECT COUNT(*) FROM dbo.StrategyVersion WHERE IsActive=1)=1
        AND EXISTS(SELECT 1 FROM dbo.StrategyVersion WHERE VersionId=@target AND IsActive=1)
    BEGIN COMMIT; RETURN; END;
    THROW 51277, 'This selection event was used earlier; review a new transition.', 1;
END;
IF (SELECT COUNT(*) FROM dbo.StrategyVersion WHERE IsActive=1)<>1
    OR NOT EXISTS(SELECT 1 FROM dbo.StrategyVersion WHERE VersionId=@previous AND IsActive=1)
    THROW 51278, 'The active predecessor changed since review.', 1;
$RegisterSql
IF NOT EXISTS(SELECT 1 FROM dbo.StrategyModelBinding WHERE StrategyVersionId=@target AND ModelSetSha256='$TargetHash')
    THROW 51279, 'Target model assignment is missing or changed.', 1;
UPDATE dbo.StrategyVersion SET IsActive=0 WHERE VersionId=@previous AND IsActive=1;
UPDATE dbo.StrategyVersion SET IsActive=1 WHERE VersionId=@target AND IsActive=0;
IF @@ROWCOUNT<>1 THROW 51280, 'Target strategy was not selected exactly once.', 1;
INSERT dbo.StrategyModelSelectionEvent(EventId,PreviousStrategyVersionId,SelectedStrategyVersionId,SelectedModelSetSha256,ReviewReference)
    VALUES(@event,@previous,@target,'$TargetHash',N'$Review');
IF `$(CommitSelection)=1 COMMIT ELSE ROLLBACK;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK;
    THROW;
END CATCH;
"@
}
$selectionSql = Selection-Sql $previousId $correctedId ([Guid]$plan.SelectionEventId) $correctedHash $registration $reviewSql
$rollbackSql = Selection-Sql $correctedId $previousId ([Guid]$plan.RollbackEventId) $previousHash '' "Explicit rollback after review; $reviewSql"
Write-NewText (Join-Path $reviewRoot 'selection.sql') $selectionSql
Write-NewText (Join-Path $reviewRoot 'rollback.sql') $rollbackSql
if ($Mode -eq 'Prepare') { Write-Output 'Prepared selection.sql and rollback.sql; no database change.'; return }

$nightlyLockPath = Join-Path $env:LOCALAPPDATA 'TraderVI\Nightly\nightly.lock'
$nightlyLock = [IO.File]::Open($nightlyLockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $scriptFile = if ($Mode -in @('Rollback', 'VerifyRollbackTransaction')) { 'rollback.sql' } else { 'selection.sql' }
    $commit = if ($Mode -in @('VerifyTransaction', 'VerifyRollbackTransaction')) { 0 } else { 1 }
    & sqlcmd -S localhost -d TraderDB -E -b -i (Join-Path $reviewRoot $scriptFile) -v "CommitSelection=$commit" -o (Join-Path $reviewRoot "$Mode.log")
    if ($LASTEXITCODE -ne 0) { throw "Model selection $Mode failed; inspect its private SQL log. No success claimed." }
    Write-Output "Model selection $Mode completed; commit=$commit. No application workflow was launched."
} finally { $nightlyLock.Dispose() }
