using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Primitives;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace CK.AspNet.Tester
{
    public class BasicMemoryFileProvider : IFileProvider
    {
        class File : IFileInfo
        {
            readonly string _name;
            DateTimeOffset _lastModified;
            byte[] _content;

            public File( string name, byte[] content)
            {
                _name = name;
                _lastModified = DateTimeOffset.UtcNow;
                _content = content;
            }

            public bool Exists => true;

            public long Length => _content.Length;

            public string PhysicalPath => _name;

            public string Name => _name;

            public DateTimeOffset LastModified => _lastModified;

            public bool IsDirectory => false;

            public Stream CreateReadStream() => new MemoryStream( _content );

        }

        readonly Dictionary<string, File> _files = new Dictionary<string, File>();
        CancellationTokenSource _changeSource = new CancellationTokenSource();

        public void Set( string name, byte[] content )
        {
            _files[name] = new File( name, content );
            RaiseChange();
        }

        public void Set( string name, string textContent ) => Set( name, Encoding.Default.GetBytes( textContent ) );

        public void Delete( string name )
        {
            _files.Remove( name );
            RaiseChange();
        }

        void RaiseChange()
        {
            var prev = _changeSource;
            _changeSource = new CancellationTokenSource();
            prev.Cancel();
            prev.Dispose();
        }

        public IDirectoryContents GetDirectoryContents( string subpath )
        {
            return NotFoundDirectoryContents.Singleton;
        }

        public IFileInfo GetFileInfo( string subpath )
        {
            File f;
            _files.TryGetValue( subpath, out f );
            return f;
        }

        public IChangeToken Watch( string filter )
        {
            return new CancellationChangeToken( _changeSource.Token );
        }
    }
}
