namespace com.m365may.utils {

    class Constants {

        public const string REDIRECT_JS = @"
<script>

    function load(url, method, callback) {
        var xhr;
            
        if(typeof XMLHttpRequest !== 'undefined') xhr = new XMLHttpRequest();
        else {
            var versions = ['MSXML2.XmlHttp.5.0', 
                            'MSXML2.XmlHttp.4.0',
                            'MSXML2.XmlHttp.3.0', 
                            'MSXML2.XmlHttp.2.0',
                            'Microsoft.XmlHttp']

                for(var i = 0, len = versions.length; i < len; i++) {
                try {
                    xhr = new ActiveXObject(versions[i]);
                    break;
                }
                catch(e){}
                } // end for
        }
            
        xhr.onreadystatechange = ensureReadiness;
            
        function ensureReadiness() {
            if(xhr.readyState < 4) {
                return;
            }
                
            if(xhr.status !== 200) {
                return;
            }

            // all is well  
            if(xhr.readyState === 4) {
                callback(xhr);
            }           
        }
            
        xhr.open(method, url, true);
        xhr.send('');
    }
    window.setInterval(function() {
        load('{url}', 'GET', function(xhr) {
            console.log('{url}', xhr.status);
        });
    }, 10000);

</script>";

    }

}