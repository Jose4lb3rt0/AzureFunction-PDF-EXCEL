using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Data.SqlClient;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models; 

namespace FunctionApp1
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // Poniendole las cadenas de conexión
            string connectionString = Environment.GetEnvironmentVariable("SqlCS");
            string blobConnectionString = Environment.GetEnvironmentVariable("BsCS");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                // Acá va la consulta
                var query = "SELECT * FROM dbo.Ventas";
                SqlCommand cmd = new SqlCommand(query, conn);
                SqlDataReader reader = await cmd.ExecuteReaderAsync();

                //------------------------PDF------------------------
                    // PDF CREACIÓN DEL ARCHIVO COMO TAL
                    Document pdfDoc = new Document();
                    MemoryStream memoryStream = new MemoryStream();
                    PdfWriter writer = PdfWriter.GetInstance(pdfDoc, memoryStream);
                    pdfDoc.Open();

                    // PDF IMPRESIÓN DEL CONTENIDO
                    pdfDoc.Add(new Paragraph("Datos de la Base de Datos"));
                    pdfDoc.Add(new Paragraph(" "));
                    while (await reader.ReadAsync())
                    {
                        // LECTURA A PARTIR DE LAS COLUMNAS DE LA TABLA
                        string venta_id = reader["venta_id"].ToString();
                        string cliente_id = reader["cliente_id"].ToString();
                        string vendedor_id = reader["vendedor_id"].ToString();
                        string producto_id = reader["producto_id"].ToString();
                        string cantidad = reader["cantidad"].ToString();
                        string precio_unitario = reader["precio_unitario"].ToString();
                        string fecha_venta = reader["fecha_venta"].ToString();
                        pdfDoc.Add(new Paragraph($"Venta ID: {venta_id}, Cliente ID: {cliente_id}, Vendedor ID: {vendedor_id}, Producto ID: {producto_id}, Cantidad: {cantidad}, Precio Unitario: {precio_unitario}, Fecha de Venta: {fecha_venta}"));
                    }
                     pdfDoc.Close();
                    byte[] pdfBytes = memoryStream.ToArray();
                    memoryStream.Close();
                //---------------------------------------------------

                // SUBIDA A BLOB STORAGE (IMPORTANTE: PONER NOMBRE DEL CONTENEDOR)
                BlobServiceClient blobServiceClient = new BlobServiceClient(blobConnectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("pdfcontainer");
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

                // NOMBRE PARA EL ARCHIVO
                string fileName = $"output-{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.pdf";
                BlobClient blobClient = containerClient.GetBlobClient(fileName);

                //SUBIDA
                using (MemoryStream uploadStream = new MemoryStream(pdfBytes))
                {
                    await blobClient.UploadAsync(uploadStream, true);
                }
            }
            //RESPUESTA DE LA FUNCIÓN POR EL LOCALHOST
            return new OkObjectResult("PDF generado y guardado en Blob Storage exitosamente.");
        }
    }
}
