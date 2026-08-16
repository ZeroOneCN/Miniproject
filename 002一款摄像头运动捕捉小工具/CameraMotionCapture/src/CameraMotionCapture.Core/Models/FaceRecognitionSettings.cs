namespace CameraMotionCapture.Core.Models;

public class FaceRecognitionSettings
{
    public bool Enabled { get; set; } = false;
    public double ConfidenceThreshold { get; set; } = 80.0;
    public string CascadeFilePath { get; set; } = "haarcascade_frontalface_default.xml";
    public string KnownFacesDir { get; set; } = "known_faces";
}