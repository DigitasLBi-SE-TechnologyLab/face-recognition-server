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

namespace FaceRecognition.Web.Controllers
{
    [RoutePrefix("api/face")]
    public class FaceController : ApiController
    {
        [HttpPost]
        [Route("learn/{user}")]
        public void SaveFaceForUser(string user, [FromBody]string image)
        {
            var userPath = HostingEnvironment.MapPath(Path.Combine("~/App_Data/users", user));

            Directory.CreateDirectory(userPath);
            var imageData = Convert.FromBase64String(image.Substring(23));
            var original = Mat.FromStream(new MemoryStream(imageData), LoadMode.AnyColor);

            // TODO: Find faces and save them intead of the whole picture
            CascadeClassifier haar_cascade = new CascadeClassifier(HostingEnvironment.MapPath("~/App_Data/cascades/haarcascade_frontalface_alt2.xml"));

            var gray = original.CvtColor(ColorConversion.BgrToGray);

            var faces = haar_cascade.DetectMultiScale(gray);
            foreach (var faceRect in faces.OrderByDescending(p => p.Width * p.Height))
            {
                var face = gray.SubMat(faceRect);
                var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                faceResized.SaveImage(Path.Combine(userPath, DateTime.Now.ToBinary() + ".jpg"));
                break;
            }
        }

        [HttpPost]
        [Route("detect")]
        public Models.User[] DetectUser([FromBody]string image)
        {
            var root = HostingEnvironment.MapPath("~/App_Data/users");

            if (!Directory.Exists(root))
                return null;

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

            var indexToName = new[] { "Unknown" }.Concat(Directory.EnumerateDirectories(root)
                .OrderBy(p => p)
                .Select(Path.GetFileName))
                .ToArray();

            var recognizer = FaceRecognizer.CreateEigenFaceRecognizer();
            var sw = new Stopwatch();
            sw.Start();
            recognizer.Train(images, labels);
            sw.Stop();
            Debug.WriteLine("training " + sw.ElapsedMilliseconds);

            CascadeClassifier haar_cascade = new CascadeClassifier(HostingEnvironment.MapPath("~/App_Data/cascades/haarcascade_frontalface_alt2.xml"));

            sw.Start();

            var imageData = Convert.FromBase64String(image.Substring(23));
            var original = Mat.FromStream(new MemoryStream(imageData), LoadMode.AnyColor);
            var gray = original.CvtColor(ColorConversion.BgrToGray);

            var users = new List<Models.User>();

            var faces = haar_cascade.DetectMultiScale(gray);
            foreach (var faceRect in faces)
            {
                var face = gray.SubMat(faceRect);
                var faceResized = face.Resize(new OpenCvSharp.CPlusPlus.Size(100, 100), 1, 1, Interpolation.Cubic);

                int label;
                double confidence;
                recognizer.Predict(faceResized, out label, out confidence);

                Debug.WriteLine("{0} {1}", label, confidence);
                users.Add(new Models.User(indexToName[label], confidence));
            }

            return users
                .OrderByDescending(p => p.Confidence)
                .ToArray();
        }

        [HttpGet]
        [Route("image/{user}")]
        public HttpResponseMessage GetImageForUser(string user)
        {
            var root = HostingEnvironment.MapPath("~/App_Data/users/" + user);
            var photo = Directory.EnumerateFiles(root, "*.jpg", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (photo == null)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(File.ReadAllBytes(photo));
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");

            return response;
        }
    }
}
