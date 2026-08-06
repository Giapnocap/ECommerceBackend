using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.API.Adapters
{
    internal sealed class FormFileUpload : IUploadFile
    {
        private readonly IFormFile _file;

        public FormFileUpload(IFormFile file)
        {
            _file = file;
        }

        public string FileName => _file.FileName;
        public string ContentType => _file.ContentType;
        public long Length => _file.Length;

        public Stream OpenReadStream() => _file.OpenReadStream();
    }
}
