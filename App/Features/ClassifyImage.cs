using System;
using System.Drawing;
using System.Linq;

namespace IOApp.Features
{
    internal class ClassifyImage
    {
        public static void LoadTensorFlow(ThumbnailItem thumbnailItem)
        {
            var modelPath = "D:\\WorkSpaceFor_Y3\\SE122 - Project 2\\Remove-Logos-and-Objects\\App\\Assets\\model.tflite";
            //using (var interpreter = new Interpreter(modelPath))
            //{
            //    // Get input and output details
            //    var inputTensor = interpreter.GetInputTensorInfo();
            //    var outputTensor = interpreter.GetOutputTensorInfo();

            //    // Assuming the model has only one input and one output
            //    var inputShape = inputTensor.Shape;
            //    var outputShape = outputTensor.Shape;

            //    // Preprocess the input image
            //    var inputImage = LoadAndPreprocessImage(thumbnailItem.InputFilePath, inputShape);

            //    // Run inference
            //    interpreter.SetInputTensorData(0, inputImage);
            //    interpreter.Invoke();

            //    // Get the inference results
            //    var outputData = interpreter.GetOutputTensorData(0);

            //    // Post-process the results (e.g., find the class with the highest probability)
            //    var result = PostprocessResults(outputData);
            //}
        }

        static float[] LoadAndPreprocessImage(string imagePath, int[] inputShape)
        {
            // Load the image
            var image = new Bitmap(imagePath);

            // Resize the image to match the input shape
            var resizedImage = new Bitmap(image, new Size(inputShape[2], inputShape[1]));

            // Convert the image to a float array
            var inputImage = GetImagePixels(resizedImage, mean: 0.0f, scale: 1.0f);

            return inputImage;
        }

        static int PostprocessResults(float[] outputData)
        {
            // Post-process the output data (customize based on your model)
            // For example, find the index of the maximum value
            var maxIndex = Array.IndexOf(outputData, outputData.Max());
            return maxIndex;
        }

        public static float[] GetImagePixels(Bitmap image, float mean, float scale)
        {
            // Ensure the image is not null
            if (image == null)
            {
                throw new ArgumentNullException(nameof(image));
            }

            // Get the width and height of the image
            int width = image.Width;
            int height = image.Height;

            // Initialize the float array for storing the pixel values
            float[] pixels = new float[width * height * 3];

            // Iterate over each pixel in the image
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Get the color of the current pixel
                    Color color = image.GetPixel(x, y);

                    // Normalize the pixel values to the range [0, 1]
                    float normalizedR = color.R / 255.0f;
                    float normalizedG = color.G / 255.0f;
                    float normalizedB = color.B / 255.0f;

                    // Apply mean and scale to the normalized values
                    float preprocessedR = (normalizedR - mean) / scale;
                    float preprocessedG = (normalizedG - mean) / scale;
                    float preprocessedB = (normalizedB - mean) / scale;

                    // Store the preprocessed values in the float array
                    int index = (y * width + x) * 3;
                    pixels[index] = preprocessedR;
                    pixels[index + 1] = preprocessedG;
                    pixels[index + 2] = preprocessedB;
                }
            }

            return pixels;
        }
    }
}