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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            // OBTENCIÓN DE PARÁMETROS
            string startDateStr = req.Query["startDate"];
            string endDateStr = req.Query["endDate"];

            DateTime startDate;
            DateTime endDate;

            if (string.IsNullOrEmpty(startDateStr) || string.IsNullOrEmpty(endDateStr) ||
                !DateTime.TryParse(startDateStr, out startDate) ||
                !DateTime.TryParse(endDateStr, out endDate))
            {
                return new BadRequestObjectResult("Proporcione parámetros válidos ?startDate='yyyy-mm-dd' y &endDate='yyyy-mm-dd'.");
            }

            // Poniendo las cadenas de conexión de local.settings.json
            string connectionString = Environment.GetEnvironmentVariable("SqlCS");
            string blobConnectionString = Environment.GetEnvironmentVariable("BsCS");

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                // Consulta SQL con cláusula BETWEEN
                var query = @"
                    SELECT v.venta_id, v.cliente_id, v.vendedor_id, v.producto_id, v.cantidad, v.precio_unitario, v.fecha_venta, 
                           c.nombre AS cliente_nombre, p.nombre AS producto_nombre, vdr.nombre AS vendedor_nombre
                    FROM dbo.Ventas v
                    INNER JOIN dbo.Clientes c ON v.cliente_id = c.clientes_id
                    INNER JOIN dbo.Productos p ON v.producto_id = p.id
                    INNER JOIN dbo.Vendedores vdr ON v.vendedor_id = vdr.vendedor_id
                    WHERE v.fecha_venta BETWEEN @startDate AND @endDate";

                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@startDate", startDate);
                cmd.Parameters.AddWithValue("@endDate", endDate);

                SqlDataReader reader = await cmd.ExecuteReaderAsync();

                //------------------------PDF------------------------
                    // PDF CREACIÓN DEL ARCHIVO COMO TAL
                    Document pdfDoc = new Document();
                    MemoryStream memoryStream = new MemoryStream();
                    PdfWriter writer = PdfWriter.GetInstance(pdfDoc, memoryStream);
                    pdfDoc.Open();

                    //ARQUITECTURA DE LA TABLA
                    PdfPTable table = new PdfPTable(10);
                    table.WidthPercentage = 100;
                    table.SetWidths(new float[] { 1f, 1.5f, 1f, 1.5f, 1f, 1.5f, 1f, 1f, 1f, 1.5f }); 
                    //CABECERA DE LA TABLA
                    string[] headers = { "Venta ID", "Cliente ID", "Cliente Nombre", "Vendedor ID", "Vendedor Nombre", "Producto ID", "Producto Nombre", "Cantidad", "Precio Unitario", "Fecha de Venta" };
                    foreach (var header in headers)
                    {
                        PdfPCell cell = new PdfPCell(new Phrase(header, FontFactory.GetFont(FontFactory.HELVETICA_BOLD, 12f, BaseColor.WHITE)));
                        cell.BackgroundColor = BaseColor.DARK_GRAY;
                        cell.HorizontalAlignment = Element.ALIGN_CENTER;
                        cell.Padding = 5;
                        table.AddCell(cell);
                    }

                    // FILAS DE LA TABLA
                    while (await reader.ReadAsync())
                    {
                        table.AddCell(CreateCell(reader["venta_id"].ToString()));
                        table.AddCell(CreateCell(reader["cliente_id"].ToString()));
                        table.AddCell(CreateCell(reader["cliente_nombre"].ToString()));
                        table.AddCell(CreateCell(reader["vendedor_id"].ToString()));
                        table.AddCell(CreateCell(reader["vendedor_nombre"].ToString()));
                        table.AddCell(CreateCell(reader["producto_id"].ToString()));
                        table.AddCell(CreateCell(reader["producto_nombre"].ToString()));
                        table.AddCell(CreateCell(reader["cantidad"].ToString()));
                        table.AddCell(CreateCell(reader["precio_unitario"].ToString()));
                        table.AddCell(CreateCell(reader["fecha_venta"].ToString()));
                    }

                    pdfDoc.Add(table);
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

                // SUBIDA
                using (MemoryStream uploadStream = new MemoryStream(pdfBytes))
                {
                    await blobClient.UploadAsync(uploadStream, true);
                }
            }

            // RESPUESTA DE LA FUNCIÓN POR EL LOCALHOST
            return new OkObjectResult("PDF generado y guardado en Blob Storage exitosamente.");
        }

        // crear celdas con estilo
        private static PdfPCell CreateCell(string text)
        {
            PdfPCell cell = new PdfPCell(new Phrase(text, FontFactory.GetFont(FontFactory.HELVETICA, 10f)));
            cell.HorizontalAlignment = Element.ALIGN_CENTER;
            cell.VerticalAlignment = Element.ALIGN_MIDDLE;
            cell.Padding = 5;
            cell.BorderWidth = 0.5f;
            return cell;
        }
    }
}
