namespace JustDanceEditor.Generation;

internal sealed class MoveSignalAnalyzer(float smoothingFrequency, uint classifierFormatVersion)
{
    private const float AccelSamplesMinCountPerPartAt30Fps = 2.49f;
    private const float SafetyDelayAtMoveStartAndEndInRatio = 0.01667f;
    private const float UsablePartDurationInRatio = 1.0f - (2.0f * SafetyDelayAtMoveStartAndEndInRatio);

    private readonly float _smoothingFrequency = smoothingFrequency;
    private readonly uint _classifierFormatVersion = classifierFormatVersion;

    public (List<float> Measures, List<float> EnergyMeasures) Analyze(
        IReadOnlyList<MotionSample> samples,
        float classifierMoveDuration,
        float gameMoveDuration,
        float accelSaturationValue)
    {
        MotionAnalysisResult result = AnalyzeDetailed(samples, classifierMoveDuration, gameMoveDuration, accelSaturationValue);
        return (result.Measures.Select(static measure => measure.Value).ToList(), result.EnergyMeasures);
    }

    public MotionAnalysisResult AnalyzeDetailed(
        IReadOnlyList<MotionSample> samples,
        float classifierMoveDuration,
        float gameMoveDuration,
        float accelSaturationValue)
    {
        MoveSpaceState state = new(GetMoveAnalysisPartsCount(classifierMoveDuration), gameMoveDuration, _smoothingFrequency);
        foreach (MotionSample sample in samples)
        {
            if (sample.Time < 0.0f || sample.Time > gameMoveDuration)
                continue;

            float progressRatio = sample.Time / gameMoveDuration;
            state.UpdateFromProgressRatioAndAccels(
                progressRatio,
                Clamp(sample.AccX, -accelSaturationValue, accelSaturationValue),
                Clamp(sample.AccY, -accelSaturationValue, accelSaturationValue),
                Clamp(sample.AccZ, -accelSaturationValue, accelSaturationValue));
        }

        return state.Stop();
    }

    public MotionAnalysisResult AnalyzeProgressSamples(
        IReadOnlyList<ProgressMotionSample> samples,
        float classifierMoveDuration,
        float gameMoveDuration,
        float accelSaturationValue)
    {
        MoveSpaceState state = new(GetMoveAnalysisPartsCount(classifierMoveDuration), gameMoveDuration, _smoothingFrequency);
        foreach (ProgressMotionSample sample in samples)
        {
            if (sample.ProgressRatio is < 0.0f or > 1.0f)
                continue;

            state.UpdateFromProgressRatioAndAccels(
                sample.ProgressRatio,
                Clamp(sample.AccX, -accelSaturationValue, accelSaturationValue),
                Clamp(sample.AccY, -accelSaturationValue, accelSaturationValue),
                Clamp(sample.AccZ, -accelSaturationValue, accelSaturationValue));
        }

        return state.Stop();
    }

    private byte GetMoveAnalysisPartsCount(float moveDuration)
    {
        if (_classifierFormatVersion == 5)
            return 10;

        return (byte)(moveDuration * 30.0f / AccelSamplesMinCountPerPartAt30Fps);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (float.IsNaN(value))
            return 0.0f;
        if (float.IsPositiveInfinity(value))
            return max;
        if (float.IsNegativeInfinity(value))
            return min;

        if (value < min)
            return min;
        return value > max ? max : value;
    }

    private sealed class MoveSpaceState
    {
        private readonly float _gameMoveDuration;
        private readonly float _initialSignalSmoothingFrequency;
        private readonly List<ISignal> _signals = [];
        private readonly List<MeasureValueInPart> _measures = [];
        private readonly List<MotionAutoCorrelationSample> _autoCorrelationSamples = [];

        private readonly BaseSignal _progressRatio = new();
        private readonly BaseSignal _ax = new();
        private readonly BaseSignal _ay = new();
        private readonly BaseSignal _az = new();

        private float _signalSmoothingNextProgressRatio;
        private float _currentSignalSmoothingFrequency;
        private uint _signalSmoothingUpdatesCount;
        private float _signalSmoothingAccelXSum;
        private float _signalSmoothingAccelYSum;
        private float _signalSmoothingAccelZSum;
        private bool _firstUpdateHasOccurred;

        public MoveSpaceState(
            byte partsCount,
            float gameMoveDuration,
            float initialSignalSmoothingFrequency)
        {
            _gameMoveDuration = gameMoveDuration;
            _initialSignalSmoothingFrequency = initialSignalSmoothingFrequency;
            CreateSignalsAndMeasures(partsCount);
            Reset(partsCount);
        }

        public void UpdateFromProgressRatioAndAccels(float progressRatio, float accelX, float accelY, float accelZ)
        {
            if (_currentSignalSmoothingFrequency == -1.0f)
            {
                UpdateSignalsAndMeasures(progressRatio, accelX, accelY, accelZ);
                return;
            }

            if (progressRatio > _signalSmoothingNextProgressRatio)
            {
                float step = 1.0f / (_gameMoveDuration * _currentSignalSmoothingFrequency);
                if (progressRatio > _signalSmoothingNextProgressRatio + step || _signalSmoothingUpdatesCount == 0)
                {
                    _currentSignalSmoothingFrequency = -1.0f;
                    UpdateSignalsAndMeasures(progressRatio, accelX, accelY, accelZ);
                }
                else
                {
                    float smoothedProgressRatio = _signalSmoothingNextProgressRatio - (0.5f * step);
                    float smoothedAccelX = _signalSmoothingAccelXSum / _signalSmoothingUpdatesCount;
                    float smoothedAccelY = _signalSmoothingAccelYSum / _signalSmoothingUpdatesCount;
                    float smoothedAccelZ = _signalSmoothingAccelZSum / _signalSmoothingUpdatesCount;

                    UpdateSignalsAndMeasures(smoothedProgressRatio, smoothedAccelX, smoothedAccelY, smoothedAccelZ);

                    _signalSmoothingNextProgressRatio += step;
                    _signalSmoothingUpdatesCount = 0;
                    _signalSmoothingAccelXSum = 0.0f;
                    _signalSmoothingAccelYSum = 0.0f;
                    _signalSmoothingAccelZSum = 0.0f;
                }
            }

            if (_currentSignalSmoothingFrequency != -1.0f)
            {
                _signalSmoothingUpdatesCount++;
                _signalSmoothingAccelXSum += accelX;
                _signalSmoothingAccelYSum += accelY;
                _signalSmoothingAccelZSum += accelZ;
            }
        }

        public MotionAnalysisResult Stop()
        {
            List<MotionMeasureResult> measures = [.. _measures.Select(static measure => new MotionMeasureResult(measure.MeasureId, measure.PartPosition, measure.Value))];
            List<float> energyMeasures = [];

            float accelNormAvgValuesSum = 0.0f;
            byte accelNormAvgValuesCount = 0;
            float accelDevNormAvgValuesSum = 0.0f;
            byte accelDevNormAvgValuesCount = 0;

            foreach (MeasureValueInPart measure in _measures)
            {
                if (measure.MeasureId == MeasureIds.AccelNormAvgNp)
                {
                    accelNormAvgValuesSum += measure.Value;
                    accelNormAvgValuesCount++;
                }
                else if (measure.MeasureId == MeasureIds.AccelDevNormAvgNp)
                {
                    accelDevNormAvgValuesSum += measure.Value;
                    accelDevNormAvgValuesCount++;
                }
            }

            if (accelNormAvgValuesCount > 0)
                energyMeasures.Add(accelNormAvgValuesSum / accelNormAvgValuesCount);
            if (accelDevNormAvgValuesCount > 0)
                energyMeasures.Add(accelDevNormAvgValuesSum / accelDevNormAvgValuesCount);

            return new MotionAnalysisResult(measures, energyMeasures, [.. _autoCorrelationSamples]);
        }

        private void Reset(byte partsCount)
        {
            foreach (ISignal signal in _signals)
                signal.Reset();
            foreach (MeasureValueInPart measure in _measures)
                measure.Reset();

            _signalSmoothingNextProgressRatio = 1.0f / (_gameMoveDuration * _initialSignalSmoothingFrequency);
            _currentSignalSmoothingFrequency = _initialSignalSmoothingFrequency;
            _signalSmoothingUpdatesCount = 0;
            _signalSmoothingAccelXSum = 0.0f;
            _signalSmoothingAccelYSum = 0.0f;
            _signalSmoothingAccelZSum = 0.0f;
            _firstUpdateHasOccurred = false;
            _autoCorrelationSamples.Clear();

            if (partsCount == 0)
                throw new InvalidOperationException("Move analysis parts count cannot be zero.");
        }

        private void CreateSignalsAndMeasures(byte partsCount)
        {
            _signals.Add(_progressRatio);
            _signals.Add(_ax);
            _signals.Add(_ay);
            _signals.Add(_az);

            DerivativeSignal axDev = new(_ax, _progressRatio);
            AverageSignal axDevAvg = new(axDev);
            DerivativeSignal ayDev = new(_ay, _progressRatio);
            AverageSignal ayDevAvg = new(ayDev);
            DerivativeSignal azDev = new(_az, _progressRatio);
            AverageSignal azDevAvg = new(azDev);
            Norm3DSignal accelNorm = new(_ax, _ay, _az);
            AverageSignal accelNormAvg = new(accelNorm);
            Norm3DSignal accelDevNorm = new(axDev, ayDev, azDev);
            AverageSignal accelDevNormAvg = new(accelDevNorm);

            _signals.Add(axDev);
            _signals.Add(axDevAvg);
            _signals.Add(ayDev);
            _signals.Add(ayDevAvg);
            _signals.Add(azDev);
            _signals.Add(azDevAvg);
            _signals.Add(accelNorm);
            _signals.Add(accelNormAvg);
            _signals.Add(accelDevNorm);
            _signals.Add(accelDevNormAvg);

            AddSplitMeasures(MeasureIds.AxDevAvgDirNp, axDevAvg, partsCount);
            AddSplitMeasures(MeasureIds.AyDevAvgDirNp, ayDevAvg, partsCount);
            AddSplitMeasures(MeasureIds.AzDevAvgDirNp, azDevAvg, partsCount);
            AddSplitMeasures(MeasureIds.AccelNormAvgNp, accelNormAvg, partsCount);
            AddSplitMeasures(MeasureIds.AccelDevNormAvgNp, accelDevNormAvg, partsCount);
        }

        private void AddSplitMeasures(byte measureId, AverageSignal sourceSignal, byte partsCount)
        {
            for (byte part = 1; part <= partsCount; part++)
                _measures.Add(new MeasureValueInPart(measureId, sourceSignal, _progressRatio, part, partsCount));
        }

        private void UpdateSignalsAndMeasures(float progressRatio, float accelX, float accelY, float accelZ)
        {
            _progressRatio.SetValue(progressRatio);
            _ax.SetValue(accelX);
            _ay.SetValue(accelY);
            _az.SetValue(accelZ);

            if (_firstUpdateHasOccurred)
            {
                foreach (ISignal signal in _signals)
                    signal.Update();
                StoreAutoCorrelationSample(progressRatio, accelX, accelY, accelZ);
                foreach (MeasureValueInPart measure in _measures)
                    measure.Update();
            }
            else
            {
                foreach (ISignal signal in _signals)
                {
                    if (signal.MustUpdateFirstTimeAsNextTimes)
                        signal.Update();
                    else
                        signal.UpdateSpeciallyForFirstTime();
                }

                StoreAutoCorrelationSample(progressRatio, accelX, accelY, accelZ);

                foreach (MeasureValueInPart measure in _measures)
                {
                    if (measure.MustUpdateFirstTimeAsNextTimes)
                        measure.Update();
                    else
                        measure.UpdateSpeciallyForFirstTime();
                }

                _firstUpdateHasOccurred = true;
            }
        }

        private void StoreAutoCorrelationSample(float progressRatio, float accelX, float accelY, float accelZ)
        {
            float accelNorm = MathF.Sqrt((accelX * accelX) + (accelY * accelY) + (accelZ * accelZ));
            _autoCorrelationSamples.Add(new MotionAutoCorrelationSample(progressRatio * _gameMoveDuration, accelNorm));
        }
    }

    private static class MeasureIds
    {
        public const byte AxDevAvgDirNp = 50;
        public const byte AyDevAvgDirNp = 51;
        public const byte AzDevAvgDirNp = 52;
        public const byte AccelNormAvgNp = 56;
        public const byte AccelDevNormAvgNp = 61;
    }

    private interface ISignal
    {
        bool MustUpdateFirstTimeAsNextTimes { get; }
        bool HasValue { get; }
        float Value { get; }
        void Reset();
        void UpdateSpeciallyForFirstTime();
        void Update();
    }

    private class BaseSignal : ISignal
    {
        public bool MustUpdateFirstTimeAsNextTimes => true;
        public bool HasValue { get; private set; } = true;
        public float Value { get; protected set; }
        public virtual void Reset()
        {
            Value = 0.0f;
            HasValue = true;
        }

        public virtual void UpdateSpeciallyForFirstTime() { }
        public virtual void Update() { }
        public void SetValue(float value)
        {
            Value = value;
            HasValue = true;
        }
    }

    private sealed class DerivativeSignal : ISignal
    {
        private readonly ISignal _sourceSignal;
        private readonly ISignal _progressRatioSignal;
        private float _previousSourceSignalValue;
        private float _previousProgressRatio;

        public DerivativeSignal(ISignal sourceSignal, ISignal progressRatioSignal)
        {
            _sourceSignal = sourceSignal;
            _progressRatioSignal = progressRatioSignal;
            Reset();
        }

        public bool MustUpdateFirstTimeAsNextTimes => false;
        public bool HasValue { get; private set; }
        public float Value { get; private set; }

        public void Reset()
        {
            Value = 0.0f;
            HasValue = false;
            _previousSourceSignalValue = 0.0f;
            _previousProgressRatio = 0.0f;
        }

        public void UpdateSpeciallyForFirstTime()
        {
            _previousSourceSignalValue = _sourceSignal.Value;
            _previousProgressRatio = _progressRatioSignal.Value;
            HasValue = false;
        }

        public void Update()
        {
            float progressDelta = _progressRatioSignal.Value - _previousProgressRatio;
            if (progressDelta <= 0.0f || float.IsNaN(progressDelta) || float.IsInfinity(progressDelta))
            {
                _previousSourceSignalValue = _sourceSignal.Value;
                _previousProgressRatio = _progressRatioSignal.Value;
                HasValue = false;
                return;
            }

            Value = (_sourceSignal.Value - _previousSourceSignalValue) / progressDelta;
            _previousSourceSignalValue = _sourceSignal.Value;
            _previousProgressRatio = _progressRatioSignal.Value;
            HasValue = true;
        }
    }

    private sealed class Norm3DSignal(MoveSignalAnalyzer.ISignal x, MoveSignalAnalyzer.ISignal y, MoveSignalAnalyzer.ISignal z) : ISignal
    {
        private readonly ISignal _x = x;
        private readonly ISignal _y = y;
        private readonly ISignal _z = z;

        public bool MustUpdateFirstTimeAsNextTimes { get; } = x.MustUpdateFirstTimeAsNextTimes
                && y.MustUpdateFirstTimeAsNextTimes
                && z.MustUpdateFirstTimeAsNextTimes;
        public bool HasValue { get; private set; }
        public float Value { get; private set; }
        public void Reset()
        {
            Value = 0.0f;
            HasValue = false;
        }

        public void UpdateSpeciallyForFirstTime() { }

        public void Update()
        {
            if (!_x.HasValue || !_y.HasValue || !_z.HasValue)
            {
                HasValue = false;
                return;
            }

            Value = MathF.Sqrt((_x.Value * _x.Value) + (_y.Value * _y.Value) + (_z.Value * _z.Value));
            HasValue = true;
        }
    }

    private sealed class AverageSignal : ISignal
    {
        private readonly ISignal _sourceSignal;
        private float _sourceSignalValuesSum;
        private int _sourceSignalValuesCount;

        public AverageSignal(ISignal sourceSignal)
        {
            _sourceSignal = sourceSignal;
            MustUpdateFirstTimeAsNextTimes = sourceSignal.MustUpdateFirstTimeAsNextTimes;
            Reset();
        }

        public bool MustUpdateFirstTimeAsNextTimes { get; }
        public bool HasValue => _sourceSignalValuesCount > 0;
        public float Value { get; private set; }

        public void Reset()
        {
            Value = 0.0f;
            _sourceSignalValuesSum = 0.0f;
            _sourceSignalValuesCount = 0;
        }

        public void UpdateSpeciallyForFirstTime() { }

        public void Update()
        {
            if (!_sourceSignal.HasValue)
                return;

            _sourceSignalValuesSum += _sourceSignal.Value;
            _sourceSignalValuesCount++;
            Value = _sourceSignalValuesSum / _sourceSignalValuesCount;
        }
    }

    private sealed class MeasureValueInPart
    {
        private readonly AverageSignal _sourceSignal;
        private readonly ISignal _progressRatioSignal;
        private readonly float _partStartInRatio;
        private readonly float _partEndInRatio;
        private bool _partProcessingHasStarted;

        public MeasureValueInPart(byte measureId, AverageSignal sourceSignal, ISignal progressRatioSignal, byte partPosition, byte partsCount)
        {
            MeasureId = measureId;
            PartPosition = partPosition;
            _sourceSignal = sourceSignal;
            _progressRatioSignal = progressRatioSignal;
            MustUpdateFirstTimeAsNextTimes = sourceSignal.MustUpdateFirstTimeAsNextTimes
                && progressRatioSignal.MustUpdateFirstTimeAsNextTimes;
            float partDurationInRatio = UsablePartDurationInRatio / partsCount;
            _partStartInRatio = SafetyDelayAtMoveStartAndEndInRatio + (partDurationInRatio * (partPosition - 1));
            _partEndInRatio = _partStartInRatio + partDurationInRatio;
            Reset();
        }

        public byte MeasureId { get; }
        public byte PartPosition { get; }
        public bool MustUpdateFirstTimeAsNextTimes { get; }
        public float Value { get; private set; }

        public void Reset()
        {
            Value = 0.0f;
            _partProcessingHasStarted = false;
        }

        public void UpdateSpeciallyForFirstTime() { }

        public void Update()
        {
            float progressRatio = _progressRatioSignal.Value;
            if (progressRatio >= _partStartInRatio && progressRatio <= _partEndInRatio)
            {
                if (!_partProcessingHasStarted)
                {
                    _partProcessingHasStarted = true;
                    _sourceSignal.Reset();
                    _sourceSignal.Update();
                }

                Value = _sourceSignal.Value;
            }
        }
    }
}