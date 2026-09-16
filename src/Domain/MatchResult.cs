using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MedVision.AnnotationViewer
{
    internal sealed class MatchResult
    {
        public MatchResult()
        {
            MatchedGtIndexes = new HashSet<int>();
            MatchedPredIndexes = new HashSet<int>();
            FalsePositiveIndexes = new HashSet<int>();
            FalseNegativeIndexes = new HashSet<int>();
            PredToGtIndexes = new Dictionary<int, int>();
        }

        public HashSet<int> MatchedGtIndexes { get; private set; }

        public HashSet<int> MatchedPredIndexes { get; private set; }

        public HashSet<int> FalsePositiveIndexes { get; private set; }

        public HashSet<int> FalseNegativeIndexes { get; private set; }

        public Dictionary<int, int> PredToGtIndexes { get; private set; }

        public int TruePositiveCount { get { return MatchedPredIndexes.Count; } }

        public int FalsePositiveCount { get { return FalsePositiveIndexes.Count; } }

        public int FalseNegativeCount { get { return FalseNegativeIndexes.Count; } }
    }
}
