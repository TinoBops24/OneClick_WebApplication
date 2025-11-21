using Microsoft.AspNetCore.Mvc.RazorPages;
using OneClick_WebApp.Services;
using OneClick_WebApp.Models;
using System.Threading.Tasks;

namespace OneClick_WebApp.Pages
{
    public class AboutModel : BasePageModel
    {
        // Properties to hold the dynamic content
        public string Section1Title { get; set; }
        public string Section1Content { get; set; }
        public string Section2Title { get; set; }
        public string Section2Content { get; set; }
        public string Section3Title { get; set; }
        public string Section3Content { get; set; }

        public AboutModel(FirebaseDbService dbService) : base(dbService)
        {
        }

        public async Task OnGetAsync()
        {
            await LoadSiteSettingsAsync();

            // Load the branch configuration for About Us content
            var branchConfig = await _dbService.GetBranchConfigurationAsync();

            if (branchConfig != null)
            {
                // Map the About sections from Firestore to page properties
                Section1Title = branchConfig.AboutSection1_Title ?? "Who We Are";
                Section1Content = branchConfig.AboutSection1_Content ?? "We are a trusted partner for quality products and exceptional service.";

                Section2Title = branchConfig.AboutSection2_Title ?? "What We Do";
                Section2Content = branchConfig.AboutSection2_Content ?? "We provide comprehensive solutions to meet your needs with dedication and expertise.";

                Section3Title = branchConfig.AboutSection3_Title ?? "How We Do It";
                Section3Content = branchConfig.AboutSection3_Content ?? "Through innovation, commitment, and a customer-first approach, we deliver results that exceed expectations.";
            }
            else
            {
                // Fallback content if no configuration exists
                Section1Title = "Who We Are";
                Section1Content = "We are a trusted partner for quality products and exceptional service.";
                Section2Title = "What We Do";
                Section2Content = "We provide comprehensive solutions to meet your needs with dedication and expertise.";
                Section3Title = "How We Do It";
                Section3Content = "Through innovation, commitment, and a customer-first approach, we deliver results that exceed expectations.";
            }
        }
    }
}