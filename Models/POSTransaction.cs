using Google.Cloud.Firestore;
using OneClick_WebApp.Models.Enums;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class POSTransaction
    {
        
        [FirestoreDocumentId]
        public string Id { get; set; }

        [FirestoreProperty("transactionId")]
        public string TransactionId { get; set; }

        [FirestoreProperty("timestamp")]
        public Timestamp? Timestamp { get; set; }

        [FirestoreProperty("onlineTimestamp")]
        public Timestamp? OnlineTimestamp { get; set; }

        [FirestoreProperty("dueDate")]
        public Timestamp? DueDate { get; set; }

        
        [FirestoreProperty("clientId")]
        public string ClientId { get; set; }

        [FirestoreProperty("clientName")]
        public string ClientName { get; set; }

        [FirestoreProperty("clientNuit")]
        public string ClientNuit { get; set; }

        [FirestoreProperty("clientAddress")]
        public string ClientAddress { get; set; }

        [FirestoreProperty("clientWebName")]
        public string ClientWebName { get; set; }

        [FirestoreProperty("clientPhoneNumber")]
        public string ClientPhoneNumber { get; set; }

       
        [FirestoreProperty("stockMovements")]
        public List<StockMovement> StockMovements { get; set; } = new();

        [FirestoreProperty("movementType")]
        public string MovementType { get; set; } = "Retail Sale";

        [FirestoreProperty("grandTotal")]
        public double GrandTotal { get; set; }

        [FirestoreProperty("totalCost")]
        public double TotalCost { get; set; }

        [FirestoreProperty("discountAmount")]
        public double DiscountAmount { get; set; }

        [FirestoreProperty("amountBeforeIVA")]
        public double AmountBeforeIVA { get; set; }

        [FirestoreProperty("ivaAmount")]
        public double IVAAmount { get; set; }

        
        [FirestoreProperty("read")]
        public bool Read { get; set; }

        [FirestoreProperty("onlineCancelled")]
        public bool OnlineCancelled { get; set; }

        [FirestoreProperty("orderStatus")]
        public OnlineOrderStatus OrderStatus { get; set; }

        [FirestoreProperty("deliveryType")]
        public DeliveryType DeliveryType { get; set; } = DeliveryType.Standard;

        [FirestoreProperty("fulfillmentStatus")]
        public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Pending;

        
        [FirestoreProperty("paymentType")]
        public PaymentType PaymentType { get; set; }

        [FirestoreProperty("bankIdPaidTo")]
        public string BankIdPaidTo { get; set; }

        [FirestoreProperty("amountPaid")]
        public double AmountPaid { get; set; }

        [FirestoreProperty("amountPending")]
        public double AmountPending { get; set; }

        [FirestoreProperty("change")]
        public double Change { get; set; }

        [FirestoreProperty("payments")]
        public List<Payment> Payments { get; set; } = new();

        [FirestoreProperty("partialPayment")]
        public PartialType PartialPayment { get; set; }

        [FirestoreProperty("proofOfPaymentAttachment")]
        public string ProofOfPaymentAttachment { get; set; }

        
        [FirestoreProperty("instructions")]
        public string Instructions { get; set; }

        [FirestoreProperty("salesRep")]
        public string SalesRep { get; set; }

        [FirestoreProperty("receiptPrinted")]
        public bool ReceiptPrinted { get; set; }

        [FirestoreProperty("invoicePrinted")]
        public bool InvoicePrinted { get; set; }

        [FirestoreProperty("packaged")]
        public bool Packaged { get; set; }

        [FirestoreProperty("readyForPickup")]
        public bool ReadyForPickup { get; set; }

        [FirestoreProperty("completionTime")]
        public Timestamp? CompletionTime { get; set; }

        
        [FirestoreProperty("branchDbName")]
        public string BranchDbName { get; set; }

        [FirestoreProperty("pickupLocation")]
        public string PickupLocation { get; set; }

        [FirestoreProperty("deliveryAddress")]
        public string DeliveryAddress { get; set; }

        
        [FirestoreProperty("officeClosingBalance")]
        public double OfficeClosingBalance { get; set; }

        [FirestoreProperty("officeOpeningBalance")]
        public double OfficeOpeningBalance { get; set; }

        [FirestoreProperty("clientClosingBalance")]
        public double ClientClosingBalance { get; set; }

        [FirestoreProperty("clientOpeningBalance")]
        public double ClientOpeningBalance { get; set; }

        [FirestoreProperty("supplierClosingBalance")]
        public double SupplierClosingBalance { get; set; }

        [FirestoreProperty("supplierOpeningBalance")]
        public double SupplierOpeningBalance { get; set; }

        [FirestoreProperty("bankClosingBalance")]
        public double BankClosingBalance { get; set; }

        [FirestoreProperty("bankOpeningBalance")]
        public double BankOpeningBalance { get; set; }

       
        [FirestoreProperty("staffResponsible")]
        public UserAccount StaffResponsible { get; set; }

        [FirestoreProperty("faturaComment")]
        public Comment FaturaComment { get; set; }

        [FirestoreProperty("adminComments")]
        public List<Comment> AdminComments { get; set; } = new();

        [FirestoreProperty("reversed")]
        public bool Reversed { get; set; }

        [FirestoreProperty("production")]
        public bool Production { get; set; }

        [FirestoreProperty("guia")]
        public bool GUIA { get; set; }

        [FirestoreProperty("type")]
        public TransactionType Type { get; set; }

        [FirestoreProperty("supplierId")]
        public string SupplierId { get; set; }

        [FirestoreProperty("supplierName")]
        public string SupplierName { get; set; }

        [FirestoreProperty("ivaInInvoiceForSupplierModule")]
        public bool IVAInInvoiceForSupplierModule { get; set; }

        [FirestoreProperty("online")]
        public bool Online { get; set; }

        [FirestoreProperty("numeroDeRequisicao")]
        public string NumeroDeRequisicao { get; set; }

    
        [FirestoreProperty("phone")]
        public string Phone { get; set; }

        [FirestoreProperty("address")]
        public string Address { get; set; }

        [FirestoreProperty("remindInMinutes")]
        public int RemindInMinutes { get; set; } = 0;

        
        public double GetTotalCostPrice()
        {
            return StockMovements?
                .Where(m => m.UnitPrice > 0)
                .Sum(m => m.UnitPrice * m.Quantity) ?? 0.0;
        }

        public string ExtractIDValue()
        {
            string pattern = @"VD-(\d+)\\(\d{4})-";
            Match match = Regex.Match(TransactionId ?? Id ?? "", pattern);

            if (match.Success)
            {
                return match.Groups[1].Value + "\\" + match.Groups[2].Value;
            }
            return "Pattern not found";
        }

        public string ExtractPrefix()
        {
            var match = Regex.Match(TransactionId ?? Id ?? "", @"^[A-Z]-\d+");
            return match.Success ? match.Value : string.Empty;
        }

        public POSTransaction Clone()
        {
            return (POSTransaction)this.MemberwiseClone();
        }
    }

   
    [FirestoreData]
    public class Payment
    {
        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("paymentType")]
        public PaymentType PaymentType { get; set; }

        [FirestoreProperty("reference")]
        public string Reference { get; set; }

        [FirestoreProperty("timestamp")]
        public Timestamp Timestamp { get; set; }
    }

    [FirestoreData]
    public class Comment
    {
        [FirestoreProperty("message")]
        public string Message { get; set; }

        [FirestoreProperty("author")]
        public string Author { get; set; }

        [FirestoreProperty("timestamp")]
        public Timestamp Timestamp { get; set; }
    }
}