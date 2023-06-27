using AADResearch.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AADResearch
{
    public class APIWrapper
    {
        private readonly HttpClient _httpClient;

        public APIWrapper(string endpoint)
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(endpoint);
            _httpClient.Timeout = TimeSpan.FromMilliseconds(60000);
        }

        public async Task<List<VolumeData>> GetVolumeData()
        {
            var result = await _httpClient.GetAsync("/api/api_data.php");

            if (result.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            // deserialize string content in VolumeData list and return it
            return null;
        }


    }
}
