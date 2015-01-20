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

using Newtonsoft.Json;

namespace FaceRecognition.Web.Controllers
{
    [RoutePrefix("api/face")]
    public class FaceController : ApiController
    {
        /**
         * Config
         */
        private ConfigJSonObject Config
        {
            // Todo: Throw errors on corrupt settings
            get
            { 
                var config = JsonConvert.DeserializeObject<ConfigJSonObject>(File.ReadAllText(HostingEnvironment.MapPath("~/App_Data/server_config.json")));
                return config;
            }
        }
        class ConfigJSonObject
        {
            public string haarcascade;
            public string recognizer;
            public int recognizer_num_components;
            public double recognizer_threshold;
            public int face_rectangle_min_width;
            public int face_rectangle_min_height;
            public int recognizer_boundary_x;
            public int recognizer_boundary_y;
            public int recognizer_boundary_width;
            public int recognizer_boundary_height;
        }
        


        private static String[] IndicesToNames = new String[0];

        private String UsersRoot { get { return HostingEnvironment.MapPath("~/App_Data/users"); } }
        private String DebugRoot { get { return HostingEnvironment.MapPath("~/App_Data/debug"); } }

        // Flag for if recognizer is trained
        private static bool isTrained = false;

        // Locks (should not be necessary)
        private readonly static Object recognizerLock = new Object();
        private readonly static Object cascadeLock = new Object();
        private readonly static Object streamLock = new Object();


        private CascadeClassifier Cascade
        {
            get
            {
                var cascade = Config.haarcascade;
                return new CascadeClassifier(HostingEnvironment.MapPath("~/App_Data/cascades/" + cascade));
            }
        }
        private FaceRecognizer Recognizer
        {
            get
            {
                var recognizer = Config.recognizer;
                switch (recognizer)
                {
                    case "fisher":
                        return FisherRecognizer;
                    case "eigen":
                    default:
                        return EigenRecognizer;
                }
            }
        }

        private static FaceRecognizer _eigenRecognizer = null;
        private static FaceRecognizer _fisherRecognizer = null;

        private FaceRecognizer EigenRecognizer
        {
            get
            {
                if (_eigenRecognizer == null)
                {
                    var config = Config;
                    _eigenRecognizer = FaceRecognizer.CreateEigenFaceRecognizer();
                }
                return _eigenRecognizer;
            }
        }
        private FaceRecognizer FisherRecognizer
        {
            get
            {
                if (_fisherRecognizer == null) _fisherRecognizer = FaceRecognizer.CreateFisherFaceRecognizer(0);
                return _fisherRecognizer;
            }
        }


        private Rect RecognizerBoundaryRect
        {
            get
            {
                var config = Config;
                return new Rect(config.recognizer_boundary_x, config.recognizer_boundary_y, config.recognizer_boundary_width, config.recognizer_boundary_height);
            }
        }



        public void TrainRecognizer()
        {
            var root = UsersRoot;

            if (!Directory.Exists(root))
                return;

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


            Recognizer.Train(images, labels);

            // Implement to save and load trained recognizer
            // Recognizer.Save("filename.yml")
            // Recognizer.Load("filename.yml")

            isTrained = true;
        }


        [HttpPost]
        [Route("learn/{user}")]
        public void SaveFaceForUser(string user, [FromBody]string imageBase64)
        {
            var userPath = Path.Combine(UsersRoot, user);

            Directory.CreateDirectory(userPath);

            var image = GetImageFromBase64(imageBase64);
            var gray = image.CvtColor(ColorConversion.BgrToGray);

            Rect[] faces = GetFacesInImage(gray);

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


        /**
         * 
         */
        [HttpPost]
        [Route("detect")]
        public Models.User[] DetectUser([FromBody]string imageBase64)
        {
            var image = GetImageFromBase64(imageBase64);
            var debugImage = image.Clone();
            if (image == null)
            {
                return new Models.User[0];
            }
            var gray = image.CvtColor(ColorConversion.BgrToGray);

            Rect[] faces = GetFacesInImage(gray);
            Rect[] filtered = FilterFaceRects(faces);

            // Write debug info to image
            debugImage.Rectangle(RecognizerBoundaryRect, new Scalar(17, 17, 17), 1); // bondary rect
            foreach (var face in faces)
            {
                debugImage.Rectangle(face, new Scalar(255, 133, 27), 1);
            }

            var users = new List<Models.User>();
            
            lock (recognizerLock)
            {
                if (!isTrained)
                {
                    TrainRecognizer();
                }

                foreach (var faceRect in filtered)
                {
                    var face = gray.SubMat(faceRect);
                    var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                    int label;
                    double confidence;
                    Recognizer.Predict(faceResized, out label, out confidence);

                    var name = IndicesToNames[label];

                    Debug.WriteLine("{0} {1}", label, confidence);
                    users.Add(new Models.User(name, confidence));

                    var text = name + ":" + ((int)confidence).ToString() + ", w:" + faceRect.Width + ", h:" + faceRect.Height;
                    debugImage.Rectangle(faceRect, new Scalar(0, 255, 0), 3);
                    debugImage.PutText(text, faceRect.Location, FontFace.HersheyPlain, 1, new Scalar(0, 255, 0));
                }
            }

            if (filtered.Length > 0)
            {
                WriteHitImage(debugImage);
            }
            WriteDebugTestingImage(debugImage);


            return users
                .OrderByDescending(p => p.Confidence)
                .ToArray();
        }



        /**
         * Returns the first image found of user
         */
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


        [HttpGet]
        [Route("latest_try")]
        public HttpResponseMessage GetLatestImageTry()
        {
            var response = Request.CreateResponse(HttpStatusCode.OK);

            var root = Path.Combine(DebugRoot, "all");

            var image = Directory.EnumerateFiles(root, "*.jpg")
                .OrderBy(f => f)
                .LastOrDefault();

            response.Content = new ByteArrayContent(File.ReadAllBytes(image));
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

            return response;
        }



        
        private Mat GetImageFromBase64(string imageStr)
        {
            var imageData = Convert.FromBase64String(imageStr);
            Mat image;
            lock (streamLock)
            {
                image = Mat.FromStream(new MemoryStream(imageData), LoadMode.AnyColor);
            }
            return image;
        }

        private Rect[] GetFacesInImage(Mat image)
        {
            Rect[] faces;
            lock (cascadeLock)
            {
                faces = Cascade.DetectMultiScale(image);
            }

            return faces;
        }

        private Rect[] FilterFaceRects(Rect[] faces)
        {
            var config = Config;
            var threshold_width = config.face_rectangle_min_width;
            var threshold_height = config.face_rectangle_min_height;
            var r = RecognizerBoundaryRect;
            var filtered = faces
                // Filter by faces contained inside rect
                .Where(face => face.Left > r.Left && face.Right < r.Right && face.Top > r.Top && face.Bottom < r.Bottom)
                // Filter by size of rect
                .Where(face => face.Width >= threshold_width && face.Height >= threshold_height)
                .ToArray();

            if (faces.Length > filtered.Length)
            {
                Debug.WriteLine("filtered out " + (faces.Length - filtered.Length) + " faces because to small rectangles");
            }

            return filtered;
        }



        protected Image MatToBitmap(Mat image)
        {
            return Bitmap.FromStream(new MemoryStream(image.Resize(new OpenCvSharp.CPlusPlus.Size(image.Width, image.Height), 1, 1, Interpolation.Cubic).ToBytes()));
        }


        protected void WriteHitImage(Mat image)
        {
            var bitmap = MatToBitmap(image);

            var root = Path.Combine(DebugRoot, "hits");
            
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, DateTime.Now.Ticks.ToString() + ".jpg");
            bitmap.Save(path);
        }

        protected void WriteDebugTestingImage(Mat image)
        {
            var bitmap = MatToBitmap(image);

            var root = Path.Combine(DebugRoot, "all");
            Directory.CreateDirectory(root);


            // remove oldest if there's more than n images
            int max_test_images = 10;
            var images = Directory.EnumerateFiles(root, "*.jpg")
                .OrderBy(f => f)
                .ToArray();
            for (int i = 0; images.Length - i >= max_test_images; i++)
            {
                File.Delete(images[i]);
            }

            // save image
            var newpath = Path.Combine(root, DateTime.Now.Ticks.ToString() + ".jpg");
            bitmap.Save(newpath);
        }
    }
}
