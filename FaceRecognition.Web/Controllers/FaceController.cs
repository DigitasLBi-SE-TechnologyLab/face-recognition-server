using OpenCvSharp;
using OpenCvSharp.CPlusPlus;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Hosting;
using System.Web.Http;
using System.Drawing;
using System.Drawing.Imaging;
using System.Web;

namespace FaceRecognition.Web.Controllers
{
    [RoutePrefix("api/face")]
    public class FaceController : ApiController
    {
        public static FaceRecognizer Recognizer = null;
        public static CascadeClassifier Cascade = null;

        protected static String[] IndicesToNames = new String[0];

        public String UsersRoot { get { return HostingEnvironment.MapPath("~/App_Data/users"); } }
        public String DebugRoot { get { return HostingEnvironment.MapPath("~/App_Data/debug"); } }

        public static bool isTrained = false;

        private readonly static Object recognizerLock = new Object();
        private readonly static Object cascadeLock = new Object();
        private readonly static Object streamLock = new Object();


        private CascadeClassifier _Cascade
        {
            get
            {
                if (Cascade == null)
                    Cascade = new CascadeClassifier(HostingEnvironment.MapPath("~/App_Data/cascades/haarcascade_frontalface_alt2.xml"));
                return Cascade;
            }
        }
        private FaceRecognizer _Recognizer
        {
            get
            {
                if (Recognizer == null)
                    Recognizer = FaceRecognizer.CreateEigenFaceRecognizer();
                return Recognizer;
            }
        }



        public void TrainRecognizer()
        {
            var root = UsersRoot;

            if (!Directory.Exists(root))
                return;

            var sw = new Stopwatch();
            sw.Start();


            var images = Directory.EnumerateDirectories(root)
                .OrderBy(p => p)
                .SelectMany(person => Directory.EnumerateFiles(person, "*.jpg")
                    .OrderBy(p => p)
                    .Select(p => Mat.FromStream(File.OpenRead(p), LoadMode.AnyColor).CvtColor(ColorConversion.BgrToGray)))
                .ToArray();

            var labels = Directory.EnumerateDirectories(root)
                .OrderBy(p => p)
                .SelectMany((person, i) => Directory.EnumerateFiles(person, "*.jpg")
                    .OrderBy(p => p)
                    .Select(_ => i + 1))
                .ToArray();

            lock (IndicesToNames)
            {
                IndicesToNames = new[] { "Unknown" }.Concat(Directory.EnumerateDirectories(root)
                    .OrderBy(p => p)
                    .Select(Path.GetFileName))
                    .ToArray();
            }


            _Recognizer.Train(images, labels);
            
            sw.Stop();
            Debug.WriteLine("TrainRecognizer: " + sw.ElapsedMilliseconds + "ms");

            isTrained = true;
        }


        [HttpPost]
        [Route("learn/{user}")]
        public void SaveFaceForUser(string user, [FromBody]string image)
        {
            var userPath = Path.Combine(UsersRoot, user);

            Directory.CreateDirectory(userPath);
            var imageData = Convert.FromBase64String(image);
            var original = Mat.FromStream(new MemoryStream(imageData), LoadMode.AnyColor);

            var gray = original.CvtColor(ColorConversion.BgrToGray);

            Rect[] faces;
            lock (cascadeLock)
            {
                faces = Cascade.DetectMultiScale(gray);
            }

            foreach (var faceRect in faces.OrderByDescending(p => p.Width * p.Height))
            {
                var face = gray.SubMat(faceRect);
                var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                faceResized.SaveImage(Path.Combine(userPath, DateTime.Now.ToBinary() + ".jpg"));
                break;
            }

            lock (recognizerLock)
            {
                TrainRecognizer();
            }
        }


        [HttpPost]
        [Route("detect")]
        public Models.User[] DetectUser([FromBody]string image)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            var imageData = Convert.FromBase64String(image);

            Mat original;
            lock (streamLock)
            {
                original = Mat.FromStream(new MemoryStream(imageData), LoadMode.AnyColor);
            }
            
            var gray = original.CvtColor(ColorConversion.BgrToGray);

            var users = new List<Models.User>();

            Rect[] faces;
            lock (cascadeLock)
            {
                faces = _Cascade.DetectMultiScale(gray);
            }
            
            lock (recognizerLock)
            {
                if (!isTrained)
                {
                    TrainRecognizer();
                }

                foreach (var faceRect in faces)
                {
                    var face = gray.SubMat(faceRect);
                    var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                    int label;
                    double confidence;
                    _Recognizer.Predict(faceResized, out label, out confidence);

                    Debug.WriteLine("{0} {1}", label, confidence);
                    users.Add(new Models.User(IndicesToNames[label], confidence));

                    original.Rectangle(faceRect, new Scalar(0, 255, 0), 3);
                    original.PutText(label.ToString(), faceRect.Location, FontFace.HersheyPlain, 1, new Scalar(0, 255, 0));
                }
            }

            if (faces.Length > 0)
            {
                WriteDebugImage(original);
            }
            

            sw.Stop();
            Debug.WriteLine("DetectUser: " + sw.ElapsedMilliseconds + "ms");

            return users
                .OrderByDescending(p => p.Confidence)
                .ToArray();
        }

        [HttpGet]
        [Route("image/{user}")]
        public HttpResponseMessage GetImageForUser(string user)
        {
            var userPath = Path.Combine(UsersRoot, user);
            var photo = Directory.EnumerateFiles(userPath, "*.jpg", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (photo == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(File.ReadAllBytes(photo));
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

            return response;
        }


        protected void WriteDebugImage(Mat image)
        {
            var bitmap = Bitmap.FromStream(new MemoryStream(image.Resize(new OpenCvSharp.CPlusPlus.Size(256, 256), 1, 1, Interpolation.Cubic).ToBytes()));

            var root = Path.Combine(DebugRoot, DateTime.Today.ToShortDateString());

            Directory.CreateDirectory(root);
            var path = Path.Combine(root, DateTime.Now.Ticks.ToString() + ".jpg");

            // bitmap.Save(path);
        }
    }
}
