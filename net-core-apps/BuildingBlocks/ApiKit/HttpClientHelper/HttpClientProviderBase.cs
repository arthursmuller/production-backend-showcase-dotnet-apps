using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace HttpClientHelper
{
	public abstract class HttpClientProviderBase
	{
		protected readonly HttpClient _httpClient;
		protected HttpClientProviderBase(HttpClient httpClient) => _httpClient = httpClient;

		public async Task<T> getAsync<T>(string endpoint)
		{
			var httpResponseMessage = await _httpClient.GetAsync(endpoint);
			return await getResponseAsync<T>(httpResponseMessage);
		}

		protected async Task<T> postAsync<T>(string endpoint, object request, bool useNamingPolicy = true, bool? useCustomSerialize = false)
		{
			var payload = fromObject(request, useNamingPolicy);
			var content = getStringContent(payload);
			var httpResponseMessage = await _httpClient.PostAsync(endpoint, content);
			return await getResponseAsync<T>(httpResponseMessage, payload, useCustomSerialize);
		}

		protected async Task<byte[]> postAsync(string endpoint, object request, bool useNamingPolicy = true)
		{
			var payload = fromObject(request, useNamingPolicy);
			var content = getStringContent(payload);
			var httpResponseMessage = await _httpClient.PostAsync(endpoint, content);
			return await getBytes(httpResponseMessage);
		}

		protected async Task<T> getResponseAsync<T>(HttpResponseMessage message, string payload = null, bool? useCustomSerialize = false)
		{
			try
			{
				message.EnsureSuccessStatusCode();
				
				if (useCustomSerialize ?? false)
					return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(await message.Content.ReadAsStringAsync());

				return await JsonSerializer.DeserializeAsync<T>(await message.Content.ReadAsStreamAsync(), new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});
			}
			catch (HttpRequestException ex)
			{
				var resPayload = await message.Content.ReadAsStringAsync();
				ex.Data.Add("res_payload", resPayload);
				ex.Data.Add("req_payload", payload);
				ex.Data.Add("status_code", message.StatusCode);
				throw;
			}
			catch (Exception ex)
			{
				ex.Data.Add("status_code", message.StatusCode);
				throw;
			}
		}
		protected async Task<byte[]> getBytes(HttpResponseMessage message)
		{
			try
			{
				message.EnsureSuccessStatusCode();

				var bytes = await message.Content.ReadAsByteArrayAsync();

				return bytes;
			}
			catch (HttpRequestException ex)
			{
				var resPayload = await message.Content.ReadAsStringAsync();
				ex.Data.Add("res_payload", resPayload);
				throw;
			}
		}

		private StringContent getStringContent(string payload) 
		{
			var content = new StringContent(
				payload,
				Encoding.UTF8,
				Application.Json);

			return content;
		}
		protected string fromObject(object payload, bool useNamingPolicy)
        {
			var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload, new Newtonsoft.Json.JsonSerializerSettings
			{
				ContractResolver = useNamingPolicy ? new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() : default,
				NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
			});

			return json;
		}
		protected T fromJson<T>(string json)
		{
            try
            {
				var res = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});

				return res;
			}
            catch
            {
				return default;
            }
		}
	}


}
