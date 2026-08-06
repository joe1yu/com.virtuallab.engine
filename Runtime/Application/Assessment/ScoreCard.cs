namespace VirtualLab.Application.Assessment
{
    public sealed class ScoreCard
    {
        public ScoreCard(int scientificResult, int operationQuality, int safety, int efficiency)
        {
            ScientificResult = Clamp(scientificResult);
            OperationQuality = Clamp(operationQuality);
            Safety = Clamp(safety);
            Efficiency = Clamp(efficiency);
        }

        public int ScientificResult { get; }

        public int OperationQuality { get; }

        public int Safety { get; }

        public int Efficiency { get; }

        public static ScoreCard Perfect => new ScoreCard(100, 100, 100, 100);

        public ScoreCard Apply(int scientificResultDelta, int operationQualityDelta, int safetyDelta, int efficiencyDelta)
        {
            return new ScoreCard(
                AddAndClamp(ScientificResult, scientificResultDelta),
                AddAndClamp(OperationQuality, operationQualityDelta),
                AddAndClamp(Safety, safetyDelta),
                AddAndClamp(Efficiency, efficiencyDelta));
        }

        private static int Clamp(int value)
        {
            return Clamp((long)value);
        }

        private static int AddAndClamp(int score, int delta)
        {
            return Clamp((long)score + delta);
        }

        private static int Clamp(long value)
        {
            if (value <= 0L)
            {
                return 0;
            }

            return value >= 100L ? 100 : (int)value;
        }
    }
}
