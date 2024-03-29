﻿using Microsoft.AspNetCore.Mvc;
using OnlineJwellery_Shopping.Models;
using OnlineJwellery_Shopping.Data;
using Microsoft.AspNetCore.Http;
using OnlineJwellery_Shopping.Models.Authentication;
using Microsoft.EntityFrameworkCore;
using BCrypt.Net;
using System;
using System.Text;
using OnlineJwellery_Shopping.Heplers;
using System.Threading.Tasks;
using System.Net;
using System.Net.Mail;
using System.Collections.Generic;


// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace OnlineJwellery_Shopping.Controllers
{
    public class CheckOutController : BaseController
    {
        private readonly JwelleryShoppingContext db;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;


        public CheckOutController(JwelleryShoppingContext context, IConfiguration configuration, IWebHostEnvironment env) : base(context)
        {
            db = context;
            _configuration = configuration;
            _env = env;
        }
        [Authentication]
        public async Task<IActionResult> Checkout()
        {
            // Kế thừa các logic chung từ BaseController
            await SetCommonViewData();

            // Lấy danh sách sản phẩm từ giỏ hàng
            List<CartItem> cartItems = HttpContext.Session.Get<List<CartItem>>("cart");
            // Truyền danh sách sản phẩm giỏ hàng cho view
            ViewBag.CartItems = cartItems;

            // Tính toán Subtotal
            decimal subtotal = cartItems.Sum(item => item.Total);
            ViewBag.Subtotal = subtotal;



            // Cộng phí thuế vào tổng số tiền
            decimal total = subtotal;
            ViewBag.Total = total;
            return View();
        }
        [HttpPost]
        [Authentication]
        public IActionResult Checkout(Order model)
        {
            if (true)
            {
                // Lấy UserId từ Session
                if (HttpContext.Session.TryGetValue("UserId", out byte[] userIdBytes) &&
                    int.TryParse(Encoding.UTF8.GetString(userIdBytes), out int userId))
                {
                    // Tạo một đối tượng Order từ dữ liệu trong form
                    var order = new Order
                    {
                        UserId = userId,
                        OrderDate = DateTime.Now,
                        Status = "pending",
                        IsPaid = "Chua thanh toan",
                        Province = model.Province,
                        District = model.District,
                        Ward = model.Ward,
                        AddressDetail = model.AddressDetail,
                        FullName = model.FullName,
                        Email = model.Email,
                        Telephone = model.Telephone,
                        PaymentMethod = model.PaymentMethod,
                        ShippingMethod = model.ShippingMethod
                    };
                    // Lấy giỏ hàng từ Session
                    List<CartItem> cartItems = HttpContext.Session.Get<List<CartItem>>("cart");
                    List<OrderProduct> orderProducts = new List<OrderProduct>();



                    // Kiểm tra xem giỏ hàng có dữ liệu không
                    if (cartItems != null && cartItems.Count > 0)
                    {
                        // Tính tổng TotalAmount từ giỏ hàng và phí vận chuyển
                        decimal subtotal = cartItems.Sum(cartItem => cartItem.Total);
                        decimal shippingFee = GetShippingFee(model.ShippingMethod); // Lấy phí vận chuyển từ hàm GetShippingFee

                        // Gán giá trị TotalAmount cho đối tượng Order
                        order.TotalAmount = subtotal + shippingFee;

                        // Lưu đơn đặt hàng vào cơ sở dữ liệu
                        _context.Order.Add(order);
                        _context.SaveChanges();
                        // Lưu thông tin sản phẩm trong đơn hàng vào bảng OrderProducts
                        foreach (var cartItem in cartItems)
                        {
                            var orderProduct = new OrderProduct
                            {
                                OrderId = order.OrderId,
                                ProductId = cartItem.ProductId,
                                Qty = cartItem.Qty,
                                Price = cartItem.Price
                            };

                            _context.OrderProduct.Add(orderProduct);
                            // Giảm số lượng sản phẩm trong bảng Product
                            var product = _context.Product.Find(cartItem.ProductId);
                            if (product != null)
                            {
                                product.Qty -= cartItem.Qty;
                                // Kiểm tra nếu muốn xử lý các điều kiện khác khi số lượng dưới 0, thì thêm điều kiện ở đây
                            }
                            // Thêm thông tin sản phẩm vào danh sách để gửi qua email
                            orderProducts.Add(orderProduct);
                        }
                        _context.SaveChanges();

                        // Xóa giỏ hàng sau khi đã đặt hàng thành công
                        HttpContext.Session.Remove("cart");

                        SendInvoiceEmail(model.Email, order, orderProducts);




                        // Thực hiện các bước xử lý thanh toán khác (nếu cần)

                        return RedirectToAction("Thankyou", "Page", new { orderId = order.OrderId, totalAmount = order.TotalAmount });
                    }
                }
            }

            // Nếu dữ liệu không hợp lệ hoặc giỏ hàng trống, quay lại trang checkout với các lỗi
            return View("Checkout", model);
        }
        // Hàm lấy phí vận chuyển dựa trên phương thức vận chuyển
        private decimal GetShippingFee(string shippingMethod)
        {
            switch (shippingMethod)
            {
                case "FastExpress":
                    return 20.00M; // Phí vận chuyển cho FastExpress 
                case "Express":
                    return 10.00M; // Phí vận chuyển cho Express
                default:
                    return 0.00M;
            }
        }
        private string GenerateEmailContent(Order order, List<OrderProduct> orderProducts)
        {
            // Đọc nội dung mẫu email từ file
            string emailTemplatePath = _env.ContentRootPath + "/Views/Email/SendEmail.cshtml";
            string emailContent = System.IO.File.ReadAllText(emailTemplatePath);

            // Thay thế các placeholder trong mẫu email bằng thông tin đơn hàng
            emailContent = emailContent.Replace("{OrderNumber}", order.OrderId.ToString());
            emailContent = emailContent.Replace("{OrderDate}", order.OrderDate.ToString("dd/MM/yyyy HH:mm"));
            emailContent = emailContent.Replace("{CustomerName}", order.FullName);
            emailContent = emailContent.Replace("{CustomerEmail}", order.Email);
            emailContent = emailContent.Replace("{ShippingMethod}", order.ShippingMethod);
            emailContent = emailContent.Replace("{PaymentMethod}", order.PaymentMethod);
            emailContent = emailContent.Replace("{Status}", order.Status);
            emailContent = emailContent.Replace("{Telephone}", order.Telephone);
            emailContent = emailContent.Replace("{ShippingAddress}", order.GetFullAddress());

            // Thêm thông tin sản phẩm vào email content
            string productList = "";
            foreach (var ordProduct in order.OrderProducts) // Đặt tên biến khác đây
            {
                productList += $"<div class='Order_list_product'><div class='Order_list_product1'><h5>{ordProduct.Product.ProductName}</h5></div><div class='quantity'><p>Qty: {ordProduct.Qty}</p></div><div class='total'><p>${ordProduct.Price}</p></div></div>";
            }
            emailContent = emailContent.Replace("{OrderProducts}", productList);

            // Thêm tổng đơn hàng vào email content
            decimal subtotal = order.OrderProducts.Sum(op => op.Price * op.Qty);
            decimal shippingFee = order.ShippingMethod == "Express" ? 10.00m : 20.00m; // Assume shipping fee is $10 for Express, $20 for other methods
            decimal totalAmount = subtotal + shippingFee;
            emailContent = emailContent.Replace("{Subtotal}", subtotal.ToString("0.00"));
            emailContent = emailContent.Replace("{ShippingFee}", shippingFee.ToString("0.00"));
            emailContent = emailContent.Replace("{TotalAmount}", totalAmount.ToString("0.00"));

            return emailContent;
        }
        private async Task SendInvoiceEmail(string recipientEmail, Order order, List<OrderProduct> orderProducts)
        {
            string emailContent = GenerateEmailContent(order, orderProducts);

            string smtpServer = _configuration["EmailSettings:SmtpServer"];
            int port = _configuration.GetValue<int>
           ("EmailSettings:Port");
            string username = _configuration["EmailSettings:Username"];
            string password = _configuration["EmailSettings:Password"];

            using (var client = new SmtpClient(smtpServer))
            {
                client.Port = port;
                client.Credentials = new System.Net.NetworkCredential(username, password);
                client.EnableSsl = true;

                var message = new MailMessage(username, recipientEmail)
                {
                    Subject = "Your Order Invoice",
                    Body = emailContent, // Nội dung email từ mẫu Razor
                    IsBodyHtml = true
                };

                try
                {
                    await client.SendMailAsync(message);
                    ViewBag.Message = "Email sent successfully";
                }
                catch (Exception ex)
                {
                    ViewBag.Error = $"Failed to send email: {ex.Message}";
                }
            }
        }
        //thankyou

        [Authentication]
        public IActionResult thankyou(int orderId)
        {
         
            return View();
        }
    }
}
