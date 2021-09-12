class MenuToggle extends HTMLElement {
  constructor() {
    super();


    // check for a Declarative Shadow Root:
    let shadow = this.shadowRoot;
    if(!shadow)
    {
      this.polyFill();
    }
    console.log(shadow);
    // this.internals = this.attachInternals();

    // // check for a Declarative Shadow Root:
    // let shadow = this.internals.shadowRoot;
    // if (!shadow) {
    //   // there wasn't one. create a new Shadow Root:
    //   // shadow = this.attachShadow({mode: 'open'});
    //   // shadow.innerHTML = `<button><slot></slot></button>`;
    // }
    // else {



    // }
  }
  polyFill(){
    
                this.querySelectorAll('template[shadowroot]').forEach(template => {
                const mode = template.getAttribute('shadowroot');
                if(!template.parentNode.shadowRoot){
                const shadowRoot = template.parentNode.attachShadow({ mode });
                shadowRoot.appendChild(template.content);
                template.remove();
                }
                });
        }
  connectedCallback() {
    let shadow = this.shadowRoot;
    const toggle = e => { console.log("toggled") };
    // in either case, wire up our event listener:
    shadow.querySelector('button').addEventListener('click', toggle);
    console.log('Custom square element added to page.');
  }
}
customElements.define('menu-toggle', MenuToggle);