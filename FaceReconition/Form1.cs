using Newtonsoft.Json.Linq;
using OpenCvSharp;
using OpenCvSharp.CPlusPlus;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FaceReconition
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            // http://docs.opencv.org/modules/contrib/doc/facerec/tutorial/facerec_video_recognition.html

            var images = Directory.EnumerateDirectories("data/people")
                .OrderBy(p => p)
                .SelectMany(person => Directory.EnumerateFiles(person, "*.pgm")
                    .OrderBy(p => p)
                    .Select(p => Mat.FromStream(File.OpenRead(p), LoadMode.AnyColor).CvtColor(ColorConversion.BgrToGray)))
                .ToArray();

            var labels = Directory.EnumerateDirectories("data/people")
                .OrderBy(p => p)
                .SelectMany((person, i) => Directory.EnumerateFiles(person, "*.pgm")
                    .OrderBy(p => p)
                    .Select(_ => i))
                .ToArray();

            var indexToName = Directory.EnumerateDirectories("data/people")
                .OrderBy(p => p)
                .Select(Path.GetFileName)
                .ToArray();

            var recognizer = FaceRecognizer.CreateEigenFaceRecognizer();
            var sw = new Stopwatch();
            sw.Start();
            recognizer.Train(images, labels);
            sw.Stop();
            Debug.WriteLine("training "+ sw.ElapsedMilliseconds);

            CascadeClassifier haar_cascade = new CascadeClassifier("cascades/haarcascade_frontalface_alt2.xml");

            var displays = new List<NetworkStream>();
            Task.Factory.StartNew(() =>
            {
                var listener = new TcpListener(IPAddress.Any, 60001);
                listener.Start();

                while (true)
                {
                    try
                    {
                        var socket = listener.AcceptSocket();
                        Debug.WriteLine(socket.RemoteEndPoint);

                        displays.Add(new NetworkStream(socket, true));
                    }
                    catch
                    { }
                }
            });

            Task.Factory.StartNew(() =>
            {
                var listener = new TcpListener(IPAddress.Any, 60000);
                listener.Start();

                while (true)
                {
                    try
                    {
                        var socket = listener.AcceptSocket();
                        Debug.WriteLine(socket.RemoteEndPoint);

                        var stream = new BufferedStream(new NetworkStream(socket, true));
                        var reader = new BinaryReader(stream);

                        while (true)
                        {
                            var data = new List<byte>();

                            var imageSize = reader.ReadInt32();
                            Debug.WriteLine("image size " + imageSize);
                            while (data.Count != imageSize)
                            {
                                var imageData = reader.ReadBytes(imageSize - data.Count);
                                data.AddRange(imageData);
                            }

                            var image = Mat.FromStream(new MemoryStream(data.ToArray()), LoadMode.AnyColor);

                            Detect(recognizer, haar_cascade, image, displays, indexToName);
                        }
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                }

            }, TaskCreationOptions.LongRunning);


            /*
            VideoCapture cap = new VideoCapture(0);
            if (!cap.IsOpened())
            {
                throw new Exception("???");
            }

            Task.Factory.StartNew(() =>
            {
                Mat frame = new Mat();
                while (true)
                {
                    cap.Read(frame);
                    if (frame.Empty())
                    {
                        Thread.Sleep(250);
                        continue;
                    }

                    var original = frame.Clone();
                    Detect(recognizer, haar_cascade, original);
                }
            }, TaskCreationOptions.LongRunning);
            */
        }

        private void Detect(FaceRecognizer recognizer, CascadeClassifier haar_cascade, Mat original, List<NetworkStream> displays, string[] indexToName)
        {
            var sw = new Stopwatch();
            sw.Start();

            var gray = original.CvtColor(ColorConversion.BgrToGray);

            var users = new List<string>();

            var faces = haar_cascade.DetectMultiScale(gray);
            foreach (var faceRect in faces)
            {
                var face = gray.SubMat(faceRect);
                var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                int label;
                double confidence;
                recognizer.Predict(faceResized, out label, out confidence);
                //if (confidence > 600)
                {
                    Debug.WriteLine("{0} {1}", label, confidence);
                    users.Add(indexToName[label]);
                }

                original.Rectangle(faceRect, new Scalar(0, 255, 0), 3);
                original.PutText(label.ToString(), faceRect.Location, FontFace.HersheyPlain, 1, new Scalar(0, 255, 0));

                // faceResized.SaveImage("data/people/hekwal/" + Guid.NewGuid() + ".jpg");
            }

            var json = JArray.FromObject(users).ToString();
            foreach (var disply in displays)
            {
                try
                {
                    var arr = BitConverter.GetBytes(json.Length);
                    disply.Write(arr, 0, arr.Length);

                    arr = Encoding.UTF8.GetBytes(json);
                    disply.Write(arr, 0, arr.Length);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            sw.Stop();
            Debug.WriteLine("Processed frame in " + sw.ElapsedMilliseconds);

            sw.Start();
            pictureBox1.Image = Bitmap.FromStream(new MemoryStream(original.Resize(new OpenCvSharp.CPlusPlus.Size(256, 256), 1, 1, Interpolation.Cubic).ToBytes()));
            // pictureBox1.Image = new Bitmap(original.Cols, original.Rows, original.ElemSize(), System.Drawing.Imaging.PixelFormat.Format24bppRgb, original.Data);
            sw.Stop();
            Debug.WriteLine("Updated UI in " + sw.ElapsedMilliseconds);
        }
    }
}
