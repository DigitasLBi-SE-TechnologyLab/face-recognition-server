namespace FaceRecognition.Web.Models
{
    public class User
    {
        public string Name { get; private set; }
        public double Confidence { get; private set; }

        public User(string name, double confidence)
        {
            this.Name = name;
            this.Confidence = confidence;
        }
    }
}