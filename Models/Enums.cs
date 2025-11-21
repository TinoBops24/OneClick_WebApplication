namespace OneClick_WebApp.Models.Enums
{
    
    public enum POSStyle
    {
        Retail
    }

   
    // Defines how products should be ordered in the UI.
   
    public enum ProductOrdering
    {
        ManualSort,
        Alphabetical,
        Categorical,
        ByCategory,
        ByPrice,
        ByPopularity,
        Custom,
        MostRecent
    }

    
    // Defines the type of stock notifications.
  
    public enum NotificationType
    {
        None,
        EndOfDay,
        OnTheSpot,
        Email,
        SMS,
        Both,
        InApp,
        Webhook
    }

    
    // Defines the status of an online order.
   
    public enum OnlineOrderStatus
    {
        NA,
        New,
        Accepted,
        Declined,
        ReadyForCollection,
        Completed,
        Pending,
        Processing,
        Incomplete,
        Cancelled
    }

  
    // Defines the access roles for users in the system.
   
    public enum Role
    {
        User = 0,           // Default role
        Customer = 1,       // Online customers
        Staff = 3,          // ERP Staff role (matches ERP role 3)
        Manager = 5,        // ERP Manager role (matches ERP role 5)
        Owner = 7,          // ERP Owners (roles 7 & 8) - using 7 as primary
        Admin = 9           // Staff
    }

    public enum ContactFormHandler
    {
        StoreInFirestore,  
        SendEmail,
        SaveToDatabase
    }

    

    public enum TransactionType
    {
        Sale,
        Return,
        Exchange,
        Refund,
        CreditNote,
        Quote
    }

    public enum DeliveryType
    {
        Standard,
        Express,
        Pickup,
        HomeDelivery,
        CurbsidePickup
    }

    public enum FulfillmentStatus
    {
        Pending,
        Processing,
        Packaged,
        ReadyForPickup,
        InTransit,
        Delivered,
        Collected,
        Cancelled
    }

    public enum PartialType
    {
        None,
        Payment,
        Items
    }

    public enum PaymentType
    {
        Cash,
        Card,
        Mobile,
        Credit,
        Mixed,
        StockTransfer,
        CreditNote,
        Quote,
        BankTransfer,
        EFT
    }
}