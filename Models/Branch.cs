using Google.Cloud.Firestore;
using Google.Cloud.Firestore.Converters;
using OneClick_WebApp.Models.Enums;
using System;
using System.Collections.Generic;

namespace OneClick_WebApp.Models
{
    [FirestoreData]
    public class Branch
    {
        
        [FirestoreProperty("companyName")]
        public string CompanyName { get; set; }

        [FirestoreProperty("businessType", ConverterType = typeof(FirestoreEnumNameConverter<POSStyle>))]
        public POSStyle BusinessType { get; set; } = POSStyle.Retail;

        [FirestoreProperty("branchNames")]
        public Dictionary<string, string> BranchNames { get; set; }

        [FirestoreProperty("printerNames")]
        public Dictionary<string, string> PrinterNames { get; set; }

        [FirestoreProperty("branchRules")]
        public Dictionary<string, Dictionary<string, bool>> BranchRules { get; set; }

        [FirestoreProperty("controlDisabled")]
        public bool ControlDisabled { get; set; }

        [FirestoreProperty("productOrderingStyle", ConverterType = typeof(FirestoreEnumNameConverter<ProductOrdering>))]
        public ProductOrdering ProductOrderingStyle { get; set; }

        [FirestoreProperty("stockDisabledRules")]
        public Dictionary<string, bool> StockDisabledRules { get; set; }

        [FirestoreProperty("branchNuit")]
        public Dictionary<string, string> BranchNuit { get; set; }

        [FirestoreProperty("branchAddress")]
        public Dictionary<string, string> BranchAddress { get; set; }

        [FirestoreProperty("ivaPercentage")]
        public double IVAPercentage { get; set; }

        [FirestoreProperty("stockNotification", ConverterType = typeof(FirestoreEnumNameConverter<NotificationType>))]
        public NotificationType StockNotification { get; set; }

        [FirestoreProperty("enableDiscount")]
        public bool EnableDiscount { get; set; }

       
        [FirestoreProperty("posIntegrationEnabled")]
        public bool PosIntegrationEnabled { get; set; } = false;

        [FirestoreProperty("posSystemType", ConverterType = typeof(FirestoreEnumNameConverter<POSSystemType>))]
        public POSSystemType PosSystemType { get; set; } = POSSystemType.Internal;

      
        [FirestoreProperty("branchId")]
        public string BranchId { get; set; } = "default_branch";

       
        [FirestoreProperty("posCollectionName")]
        [Obsolete("Use BranchId with fixed collection structure instead")]
        public string PosCollectionName { get; set; } = "POSOrders";

        [FirestoreProperty("autoCreatePosTransactions")]
        public bool AutoCreatePosTransactions { get; set; } = true;

        [FirestoreProperty("posTransactionPrefix")]
        public string PosTransactionPrefix { get; set; } = "POS";

        [FirestoreProperty("enableStockValidation")]
        public bool EnableStockValidation { get; set; } = true;

        [FirestoreProperty("posNotificationEmail")]
        public string PosNotificationEmail { get; set; }

        // Site Configuration Properties
        [FirestoreProperty("phone")]
        public string Phone { get; set; }

        [FirestoreProperty("description")]
        public string Description { get; set; }

        [FirestoreProperty("primaryColour")]
        public string PrimaryColour { get; set; }

        [FirestoreProperty("secondaryColour")]
        public string SecondaryColour { get; set; }

        [FirestoreProperty("logoUrl")]
        public string LogoUrl { get; set; }

        [FirestoreProperty("socialMediaLink")]
        public string SocialMediaLink { get; set; }

        // Structured "About Us" Content 
        [FirestoreProperty("aboutSection1_Title")]
        public string AboutSection1_Title { get; set; }

        [FirestoreProperty("aboutSection1_Content")]
        public string AboutSection1_Content { get; set; }

        [FirestoreProperty("aboutSection2_Title")]
        public string AboutSection2_Title { get; set; }

        [FirestoreProperty("aboutSection2_Content")]
        public string AboutSection2_Content { get; set; }

        [FirestoreProperty("aboutSection3_Title")]
        public string AboutSection3_Title { get; set; }

        [FirestoreProperty("aboutSection3_Content")]
        public string AboutSection3_Content { get; set; }

        // "Contact Us" Page Settings
        [FirestoreProperty("contactFormRecipientEmail")]
        public string ContactFormRecipientEmail { get; set; }

        [FirestoreProperty("contactPagePhone")]
        public string ContactPagePhone { get; set; }

        [FirestoreProperty("contactPageLocation")]
        public string ContactPageLocation { get; set; }

        [FirestoreProperty("contactFormHandler", ConverterType = typeof(FirestoreEnumNameConverter<ContactFormHandler>))]
        public ContactFormHandler ContactFormHandler { get; set; }

        // Footer Content Properties
        [FirestoreProperty("footerTagline")]
        public string FooterTagline { get; set; }

        [FirestoreProperty("supportEmail")]
        public string SupportEmail { get; set; }

        [FirestoreProperty("supportPhone")]
        public string SupportPhone { get; set; }

        [FirestoreProperty("socialMedia_Facebook")]
        public string SocialMedia_Facebook { get; set; }

        [FirestoreProperty("socialMedia_Instagram")]
        public string SocialMedia_Instagram { get; set; }

        [FirestoreProperty("socialMedia_Twitter")]
        public string SocialMedia_Twitter { get; set; }

        [FirestoreProperty("socialMedia_LinkedIn")]
        public string SocialMedia_LinkedIn { get; set; }
        [FirestoreProperty("BranchOnlineSaleToSystem")]
        public Dictionary<string, bool> BranchOnlineSaleToSystem { get; set; }
    }

   
    public enum POSSystemType
    {
        Internal,
        External_API,
        External_Database
    }
}