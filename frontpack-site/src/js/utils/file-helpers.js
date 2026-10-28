// js/utils/file-helpers.js

class FileHelper {
    readFileAsText(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            
            reader.onload = (event) => {
                try {
                    const content = event.target.result;
                    // Ensure we're returning a clean string
                    resolve(content.toString());
                } catch (error) {
                    reject(error);
                } finally {
                    reader.abort(); // Clean up
                }
            };
            
            reader.onerror = (error) => {
                reader.abort(); // Clean up on error
                reject(error);
            };
            
            reader.readAsText(file);
        });
    }
    
    readFileAsBinary(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            
            reader.onload = (event) => {
                try {
                    const arrayBuffer = event.target.result;
                    // Ensure we're creating a proper base64 string
                    const bytes = new Uint8Array(arrayBuffer);
                    const binary = Array.from(bytes)
                        .map(byte => String.fromCharCode(byte))
                        .join('');
                    const base64String = btoa(binary);
                    resolve(base64String);
                } catch (error) {
                    reject(error);
                } finally {
                    reader.abort(); // Clean up
                }
            };
            
            reader.onerror = (error) => {
                reader.abort(); // Clean up on error
                reject(error);
            };
            
            reader.readAsArrayBuffer(file);
        });
    }
}

window.fileHelper = new FileHelper();
