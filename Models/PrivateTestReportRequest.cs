using System.Collections.Generic;

namespace Limelight.Models
{
    public sealed class PrivateTestReportRequest
    {
        public string Summary { get; set; } =
            string.Empty;

        public string Area { get; set; } =
            string.Empty;

        public string ReproductionSteps { get; set; } =
            string.Empty;

        public string ExpectedResult { get; set; } =
            string.Empty;

        public string ActualResult { get; set; } =
            string.Empty;

        public string Outcome { get; set; } =
            string.Empty;

        public List<string> AttachmentPaths { get; set; } =
            new List<string>();
    }
}
