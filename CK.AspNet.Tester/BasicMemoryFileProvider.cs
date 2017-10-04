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
    /// <summary>
    /// Really stupid <see cref="IFileProvider"/> implementation meant to be used
    /// only in test environment.
    /// </summary>
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

        /// <summary>
        /// Adds or updates a file with a name and a content.
        /// Paths are not handled by this simple implementation.
        /// </summary>
        /// <param name="name">Name of the file. Must not be null.</param>
        /// <param name="content">Content. Must not be null.</param>
        public void Set( string name, byte[] content )
        {
            if( name == null ) throw new ArgumentNullException( nameof( name ) );
            if( content == null ) throw new ArgumentNullException( nameof( content ) );
            _files[name] = new File( name, content );
            RaiseChange();
        }

        /// <summary>
        /// Adds or updates a file with a name and a text content.
        /// Underlying storage is UTF-16 (<see cref="Encoding.Default"/>).
        /// Paths are not handled by this simple implementation.
        /// </summary>
        /// <param name="name">Name of the file. Must not be null.</param>
        /// <param name="textContent">Text content. Must not be null.</param>
        public void Set( string name, string textContent )
        {
            if( textContent == null ) throw new ArgumentNullException( nameof( textContent ) );
            Set( name, Encoding.Default.GetBytes( textContent ) );
        }

        /// <summary>
        /// Deletes a file previously set.
        /// </summary>
        /// <param name="name">Name of the file to delete.</param>
        public void Delete( string name )
        {
            if( _files.Remove( name ) ) RaiseChange();
        }

        void RaiseChange()
        {
            var prev = _changeSource;
            _changeSource = new CancellationTokenSource();
            prev.Cancel();
            prev.Dispose();
        }

        /// <summary>
        /// Since paths ared not handled by this simplistic implementation,
        /// always returns <see cref="NotFoundDirectoryContents.Singleton"/>.
        /// </summary>
        /// <param name="subpath">Ignored parameter.</param>
        /// <returns>Always <see cref="NotFoundDirectoryContents.Singleton"/>.</returns>
        public IDirectoryContents GetDirectoryContents( string subpath )
        {
            return NotFoundDirectoryContents.Singleton;
        }

        /// <summary>
        /// Returns a <see cref="IFileInfo"/> that may be a <see cref="NotFoundFileInfo"/>
        /// instance (this is what this abstraction requires).
        /// </summary>
        /// <param name="subpath">The path to the file.</param>
        /// <returns>The file info.</returns>
        public IFileInfo GetFileInfo( string subpath )
        {
            File f;
            _files.TryGetValue( subpath, out f );
            return f ?? (IFileInfo)new NotFoundFileInfo(subpath);
        }

        /// <summary>
        /// Gets a change token. This simplistic implementation
        /// always trigger a change, whatever the changed path is:
        /// <paramref name="filter"/> is ignored.
        /// </summary>
        /// <param name="filter">Ignored parameter.</param>
        /// <returns>A change token that will trigger on any change.</returns>
        public IChangeToken Watch( string filter )
        {
            return new CancellationChangeToken( _changeSource.Token );
        }
    }
}
