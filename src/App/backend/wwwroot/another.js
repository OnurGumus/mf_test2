class AnotherComponent extends HTMLElement {
    connectedCallback() {
      this.innerHTML = `<h1>another</h1>`;
    }
  }
      
  customElements.define('another-component', AnotherComponent);